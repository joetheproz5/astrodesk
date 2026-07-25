using AstroDesk.Device.Processes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AstroDesk.Device.Scrcpy;

public sealed class ScrcpyService : IScrcpyService
{
    private readonly IProcessRunner _processRunner;
    private readonly IExecutableLocator _executableLocator;
    private readonly IScrcpyWindowManager _windowManager;
    private readonly DeviceToolOptions _toolOptions;
    private readonly ILogger<ScrcpyService> _logger;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private IChildProcess? _process;
    private ScrcpySession? _session;
    private ScrcpyLaunchOptions? _lastOptions;
    private WindowPresentationState? _windowPresentation;
    private Task? _exitMonitorTask;
    private int _state = (int)ScrcpyState.Stopped;
    private int _disposed;

    public ScrcpyService(
        IProcessRunner processRunner,
        IExecutableLocator executableLocator,
        IScrcpyWindowManager windowManager,
        DeviceToolOptions toolOptions,
        ILogger<ScrcpyService>? logger = null)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _executableLocator = executableLocator ?? throw new ArgumentNullException(nameof(executableLocator));
        _windowManager = windowManager ?? throw new ArgumentNullException(nameof(windowManager));
        _toolOptions = toolOptions ?? throw new ArgumentNullException(nameof(toolOptions));
        _logger = logger ?? NullLogger<ScrcpyService>.Instance;
    }

    public event EventHandler<ScrcpyStateChangedEventArgs>? StateChanged;

    public event EventHandler<ScrcpyLogEventArgs>? LogReceived;

    public event EventHandler<ScrcpyCrashedEventArgs>? Crashed;

    public ScrcpyState State => (ScrcpyState)Volatile.Read(ref _state);

    public ScrcpySession? CurrentSession => Volatile.Read(ref _session);

    public async Task<ScrcpySession> StartAsync(
        ScrcpyLaunchOptions options,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(options);
        ScrcpyArgumentBuilder.Validate(options);

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        IChildProcess? startedProcess = null;
        try
        {
            if (_session is not null && _process is { HasExited: false })
            {
                return _session;
            }

            if (_process is not null)
            {
                var staleProcess = _process;
                _process = null;
                _session = null;
                _windowPresentation = null;

                await StopAndDisposeQuietlyAsync(staleProcess).ConfigureAwait(false);
            }

            SetState(ScrcpyState.Starting);
            var executablePath = _executableLocator.Resolve(
                new ExecutableRequest(
                    "scrcpy.exe",
                    options.ExecutablePath ?? _toolOptions.ScrcpyExecutablePath,
                    "ASTRODESK_SCRCPY_PATH"));
            var title = ScrcpyArgumentBuilder.CreateUniqueWindowTitle(options.WindowTitlePrefix);
            var arguments = ScrcpyArgumentBuilder.Build(options, title);
            var sensitiveValues = string.IsNullOrWhiteSpace(options.DeviceSerial)
                ? Array.Empty<string>()
                : new[] { options.DeviceSerial };

            startedProcess = await _processRunner.StartAsync(
                new ProcessInvocation(
                    executablePath,
                    arguments,
                    WorkingDirectory: Path.GetDirectoryName(executablePath),
                    EnvironmentVariables: new Dictionary<string, string?>
                    {
                        ["SDL_MOUSE_FOCUS_CLICKTHROUGH"] = "1",
                    },
                    SensitiveValues: sensitiveValues,
                    CreateNoWindow: true,
                    OutputReceived: RaiseLogReceived),
                cancellationToken).ConfigureAwait(false);

            var windowHandle = await _windowManager.FindWindowAsync(
                title,
                startedProcess.Id,
                options.WindowDiscoveryTimeout,
                cancellationToken).ConfigureAwait(false);
            if (windowHandle == nint.Zero)
            {
                throw new ScrcpyWindowNotFoundException(title, options.WindowDiscoveryTimeout);
            }

            var presentation = _windowManager.HideOffscreenWithoutMinimizing(windowHandle);
            var session = new ScrcpySession(
                startedProcess.Id,
                windowHandle,
                title,
                DateTimeOffset.UtcNow,
                options);

            _process = startedProcess;
            _session = session;
            _lastOptions = options;
            _windowPresentation = presentation;
            startedProcess = null;
            SetState(ScrcpyState.Running);
            _exitMonitorTask = MonitorUnexpectedExitAsync(_process, session);
            return session;
        }
        catch
        {
            if (startedProcess is not null)
            {
                await StopAndDisposeQuietlyAsync(startedProcess).ConfigureAwait(false);
            }

            _process = null;
            _session = null;
            _windowPresentation = null;
            SetState(ScrcpyState.Faulted);
            throw;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await StopCoreAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ScrcpySession> ReconnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var options = _lastOptions
                      ?? throw new InvalidOperationException("scrcpy has not been started, so there is no launch configuration to reconnect.");
        SetState(ScrcpyState.Reconnecting);
        await StopCoreAsync(cancellationToken).ConfigureAwait(false);
        return await StartAsync(options, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await StopCoreAsync(CancellationToken.None).ConfigureAwait(false);
        if (_exitMonitorTask is not null)
        {
            try
            {
                await _exitMonitorTask.ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "scrcpy exit monitor ended during disposal.");
            }
        }

        _lifecycleGate.Dispose();
    }

    private async Task StopCoreAsync(CancellationToken cancellationToken)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var process = _process;
            if (process is null)
            {
                _session = null;
                _windowPresentation = null;
                SetState(ScrcpyState.Stopped);
                return;
            }

            SetState(ScrcpyState.Stopping);
            _process = null;
            _session = null;
            _windowPresentation = null;

            try
            {
                await process.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await process.DisposeAsync().ConfigureAwait(false);
                SetState(ScrcpyState.Stopped);
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async Task MonitorUnexpectedExitAsync(IChildProcess process, ScrcpySession session)
    {
        int exitCode;
        try
        {
            exitCode = await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is ObjectDisposedException or InvalidOperationException)
        {
            return;
        }

        ScrcpyCrashedEventArgs? crash = null;
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!ReferenceEquals(_process, process))
            {
                return;
            }

            _process = null;
            _session = null;
            _windowPresentation = null;
            await process.DisposeAsync().ConfigureAwait(false);
            SetState(ScrcpyState.Crashed);
            crash = new ScrcpyCrashedEventArgs(session, exitCode);
        }
        finally
        {
            _lifecycleGate.Release();
        }

        _logger.LogError("scrcpy exited unexpectedly with code {ExitCode}.", exitCode);
        if (crash is not null)
        {
            RaiseCrashed(crash);
        }
    }

    private async Task StopAndDisposeQuietlyAsync(IChildProcess process)
    {
        try
        {
            await process.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Failed to stop a partially started scrcpy process.");
        }

        await process.DisposeAsync().ConfigureAwait(false);
    }

    private void SetState(ScrcpyState state)
    {
        var previous = (ScrcpyState)Interlocked.Exchange(ref _state, (int)state);
        if (previous == state)
        {
            return;
        }

        var handlers = StateChanged;
        if (handlers is null)
        {
            return;
        }

        var eventArgs = new ScrcpyStateChangedEventArgs(previous, state);
        foreach (EventHandler<ScrcpyStateChangedEventArgs> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, eventArgs);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "A scrcpy state subscriber threw an exception.");
            }
        }
    }

    private void RaiseLogReceived(ProcessOutputLine line)
    {
        var handlers = LogReceived;
        if (handlers is null)
        {
            return;
        }

        var eventArgs = new ScrcpyLogEventArgs(line);
        foreach (EventHandler<ScrcpyLogEventArgs> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, eventArgs);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "A scrcpy log subscriber threw an exception.");
            }
        }
    }

    private void RaiseCrashed(ScrcpyCrashedEventArgs eventArgs)
    {
        var handlers = Crashed;
        if (handlers is null)
        {
            return;
        }

        foreach (EventHandler<ScrcpyCrashedEventArgs> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, eventArgs);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "A scrcpy crash subscriber threw an exception.");
            }
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }
}
