using AstroDesk.Device.Processes;

namespace AstroDesk.Device.Scrcpy;

public sealed record ScrcpyLaunchOptions
{
    public string? ExecutablePath { get; init; }

    public string? DeviceSerial { get; init; }

    public int VideoBitRateMbps { get; init; } = 12;

    public int? MaxSize { get; init; } = 1440;

    public int? MaxFps { get; init; } = 60;

    public bool KeepAwake { get; init; } = true;

    public bool TurnScreenOff { get; init; }

    public string WindowTitlePrefix { get; init; } = "AstroDesk-Phone-Preview";

    public TimeSpan WindowDiscoveryTimeout { get; init; } = TimeSpan.FromSeconds(15);
}

public enum ScrcpyState
{
    Stopped,
    Starting,
    Running,
    Reconnecting,
    Stopping,
    Crashed,
    Faulted,
}

public sealed record ScrcpySession(
    int ProcessId,
    nint WindowHandle,
    string WindowTitle,
    DateTimeOffset StartedAt,
    ScrcpyLaunchOptions Options);

public sealed class ScrcpyStateChangedEventArgs : EventArgs
{
    public ScrcpyStateChangedEventArgs(ScrcpyState previousState, ScrcpyState state)
    {
        PreviousState = previousState;
        State = state;
    }

    public ScrcpyState PreviousState { get; }

    public ScrcpyState State { get; }
}

public sealed class ScrcpyLogEventArgs : EventArgs
{
    public ScrcpyLogEventArgs(ProcessOutputLine line)
    {
        Line = line;
    }

    public ProcessOutputLine Line { get; }
}

public sealed class ScrcpyCrashedEventArgs : EventArgs
{
    public ScrcpyCrashedEventArgs(ScrcpySession session, int exitCode)
    {
        Session = session;
        ExitCode = exitCode;
    }

    public ScrcpySession Session { get; }

    public int ExitCode { get; }
}

public sealed class ScrcpyWindowNotFoundException : InvalidOperationException
{
    public ScrcpyWindowNotFoundException(string title, TimeSpan timeout)
        : base($"scrcpy started, but its window '{title}' was not found within {timeout.TotalSeconds:0.#} seconds.")
    {
        WindowTitle = title;
        Timeout = timeout;
    }

    public string WindowTitle { get; }

    public TimeSpan Timeout { get; }
}

public interface IScrcpyService : IAsyncDisposable
{
    event EventHandler<ScrcpyStateChangedEventArgs>? StateChanged;

    event EventHandler<ScrcpyLogEventArgs>? LogReceived;

    event EventHandler<ScrcpyCrashedEventArgs>? Crashed;

    ScrcpyState State { get; }

    ScrcpySession? CurrentSession { get; }

    Task<ScrcpySession> StartAsync(
        ScrcpyLaunchOptions options,
        CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    Task<ScrcpySession> ReconnectAsync(CancellationToken cancellationToken = default);
}
