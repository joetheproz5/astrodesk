namespace AstroDesk.App.Services;

public enum ExposureTimerState
{
    Idle,
    Running,
    Paused,
    Completed,
}

public enum ExposureTimerPhase
{
    InitialDelay,
    Exposure,
    BetweenFrames,
    Complete,
}

public sealed record ExposureTimerOptions(
    TimeSpan ExposureDuration,
    TimeSpan DelayBetweenFrames,
    int FrameCount,
    TimeSpan InitialDelay);

public sealed record ExposureTimerTick(
    ExposureTimerState State,
    ExposureTimerPhase Phase,
    TimeSpan Remaining,
    int CurrentFrame,
    int CompletedFrames,
    int TotalFrames);

public interface IExposureTimerService : IAsyncDisposable
{
    event EventHandler<ExposureTimerTick>? Tick;

    ExposureTimerState State { get; }

    Task StartAsync(ExposureTimerOptions options, CancellationToken cancellationToken = default);

    void Pause();

    void Resume();

    Task StopAsync();
}

public sealed class ExposureTimerService : IExposureTimerService
{
    private readonly object _sync = new();
    private CancellationTokenSource? _cancellation;
    private Task? _timerTask;
    private ExposureTimerState _state = ExposureTimerState.Idle;

    public event EventHandler<ExposureTimerTick>? Tick;

    public ExposureTimerState State
    {
        get
        {
            lock (_sync)
            {
                return _state;
            }
        }
    }

    public async Task StartAsync(
        ExposureTimerOptions options,
        CancellationToken cancellationToken = default)
    {
        Validate(options);
        await StopAsync().ConfigureAwait(false);

        lock (_sync)
        {
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _state = ExposureTimerState.Running;
            _timerTask = RunAsync(options, _cancellation.Token);
        }
    }

    public void Pause()
    {
        lock (_sync)
        {
            if (_state == ExposureTimerState.Running)
            {
                _state = ExposureTimerState.Paused;
            }
        }
    }

    public void Resume()
    {
        lock (_sync)
        {
            if (_state == ExposureTimerState.Paused)
            {
                _state = ExposureTimerState.Running;
            }
        }
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? cancellation;
        Task? timerTask;
        lock (_sync)
        {
            cancellation = _cancellation;
            timerTask = _timerTask;
            _cancellation = null;
            _timerTask = null;
            _state = ExposureTimerState.Idle;
        }

        if (cancellation is null)
        {
            return;
        }

        cancellation.Cancel();
        try
        {
            if (timerTask is not null)
            {
                await timerTask.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    private async Task RunAsync(ExposureTimerOptions options, CancellationToken cancellationToken)
    {
        int completedFrames = 0;
        try
        {
            if (options.InitialDelay > TimeSpan.Zero)
            {
                await CountdownAsync(
                        ExposureTimerPhase.InitialDelay,
                        options.InitialDelay,
                        0,
                        completedFrames,
                        options.FrameCount,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            for (int frame = 1; frame <= options.FrameCount; frame++)
            {
                await CountdownAsync(
                        ExposureTimerPhase.Exposure,
                        options.ExposureDuration,
                        frame,
                        completedFrames,
                        options.FrameCount,
                        cancellationToken)
                    .ConfigureAwait(false);
                completedFrames++;
                Publish(
                    ExposureTimerPhase.Exposure,
                    TimeSpan.Zero,
                    frame,
                    completedFrames,
                    options.FrameCount);

                if (frame < options.FrameCount && options.DelayBetweenFrames > TimeSpan.Zero)
                {
                    await CountdownAsync(
                            ExposureTimerPhase.BetweenFrames,
                            options.DelayBetweenFrames,
                            frame + 1,
                            completedFrames,
                            options.FrameCount,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            lock (_sync)
            {
                if (_state != ExposureTimerState.Idle)
                {
                    _state = ExposureTimerState.Completed;
                }
            }

            Publish(
                ExposureTimerPhase.Complete,
                TimeSpan.Zero,
                options.FrameCount,
                completedFrames,
                options.FrameCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task CountdownAsync(
        ExposureTimerPhase phase,
        TimeSpan duration,
        int currentFrame,
        int completedFrames,
        int totalFrames,
        CancellationToken cancellationToken)
    {
        TimeSpan remaining = duration;
        DateTimeOffset last = DateTimeOffset.UtcNow;
        while (remaining > TimeSpan.Zero)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExposureTimerState state = State;
            if (state == ExposureTimerState.Paused)
            {
                last = DateTimeOffset.UtcNow;
                Publish(phase, remaining, currentFrame, completedFrames, totalFrames);
                await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
                continue;
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            remaining -= now - last;
            last = now;
            if (remaining < TimeSpan.Zero)
            {
                remaining = TimeSpan.Zero;
            }

            Publish(phase, remaining, currentFrame, completedFrames, totalFrames);
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
        }
    }

    private void Publish(
        ExposureTimerPhase phase,
        TimeSpan remaining,
        int currentFrame,
        int completedFrames,
        int totalFrames)
    {
        Tick?.Invoke(
            this,
            new ExposureTimerTick(
                State,
                phase,
                remaining,
                currentFrame,
                completedFrames,
                totalFrames));
    }

    private static void Validate(ExposureTimerOptions options)
    {
        if (options.ExposureDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Exposure duration must be positive.");
        }

        if (options.DelayBetweenFrames < TimeSpan.Zero || options.InitialDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Timer delays cannot be negative.");
        }

        if (options.FrameCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Timer frame count must be positive.");
        }
    }
}
