using System.Threading.Channels;
using System.Windows.Media;
using AstroDesk.Stacking;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AstroDesk.App.Services;

public sealed class LivePreviewUpdatedEventArgs(
    ImageSource image,
    int framesStacked,
    FrameOffset offset) : EventArgs
{
    public ImageSource Image { get; } = image;

    public int FramesStacked { get; } = framesStacked;

    /// <summary>How far this frame had drifted from the reference.</summary>
    public FrameOffset Offset { get; } = offset;
}

public interface ILivePreviewStackCoordinator : IAsyncDisposable
{
    event EventHandler<LivePreviewUpdatedEventArgs>? Updated;

    int FramesStacked { get; }

    /// <summary>Discards the running stack and starts a new one.</summary>
    void Reset();

    /// <summary>Queues a captured file for inclusion in the running preview.</summary>
    void Offer(string path);
}

/// <summary>
/// Keeps a running stacked preview up to date as a session captures frames.
/// </summary>
/// <remarks>
/// Decoding and aligning happen on a background worker fed by a queue, because
/// both are slow enough to stutter the UI if run on the dispatcher. The queue is
/// unbounded but that is safe here: frames arrive every twenty seconds or so,
/// far slower than a frame takes to process.
/// </remarks>
public sealed class LivePreviewStackCoordinator : ILivePreviewStackCoordinator
{
    private readonly ILivePreviewRenderer _renderer;
    private readonly ILogger<LivePreviewStackCoordinator> _logger;
    private readonly Channel<string> _queue;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _worker;
    private readonly object _sync = new();

    private LivePreviewStacker? _stacker;

    public LivePreviewStackCoordinator(
        ILivePreviewRenderer renderer,
        ILogger<LivePreviewStackCoordinator>? logger = null)
    {
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        _logger = logger ?? NullLogger<LivePreviewStackCoordinator>.Instance;
        _queue = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

        _worker = Task.Run(() => ProcessAsync(_shutdown.Token));
    }

    public event EventHandler<LivePreviewUpdatedEventArgs>? Updated;

    public int FramesStacked
    {
        get
        {
            lock (_sync)
            {
                return _stacker?.FramesStacked ?? 0;
            }
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            _stacker = null;
        }
    }

    public void Offer(string path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            _ = _queue.Writer.TryWrite(path);
        }
    }

    private async Task ProcessAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (string path in _queue.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    ProcessOne(path);
                }
                catch (Exception exception)
                {
                    // One unreadable frame must not end the preview for the run.
                    _logger.LogWarning(exception, "Live preview failed on {Path}.", path);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown.
        }
    }

    private void ProcessOne(string path)
    {
        PreviewImage? frame = _renderer.Decode(path);
        if (frame is null)
        {
            return;
        }

        LivePreviewStacker stacker;
        lock (_sync)
        {
            // The first frame fixes the working size; the phone does not change
            // resolution mid-session, and a mismatch is rejected rather than
            // silently resampled.
            _stacker ??= new LivePreviewStacker(frame.Width, frame.Height);
            stacker = _stacker;
        }

        FrameOffset offset;
        try
        {
            offset = stacker.Add(frame);
        }
        catch (ArgumentException)
        {
            // A different-sized frame means the source changed; start over
            // rather than refusing every remaining frame.
            _logger.LogInformation("Preview frame size changed; restarting the live stack.");
            lock (_sync)
            {
                _stacker = new LivePreviewStacker(frame.Width, frame.Height);
                stacker = _stacker;
            }

            offset = stacker.Add(frame);
        }

        PreviewImage? result = stacker.GetResult();
        if (result is null)
        {
            return;
        }

        ImageSource image = _renderer.Render(result);
        Updated?.Invoke(
            this,
            new LivePreviewUpdatedEventArgs(image, stacker.FramesStacked, offset));
    }

    public async ValueTask DisposeAsync()
    {
        _queue.Writer.TryComplete();
        await _shutdown.CancelAsync().ConfigureAwait(false);
        try
        {
            await _worker.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected.
        }

        _shutdown.Dispose();
    }
}
