namespace AstroDesk.Capture.Frames;

public sealed record WindowCaptureOptions
{
    public int FramesPerSecond { get; init; } = 30;

    public bool PreferPrintWindow { get; init; } = true;
}

public sealed class CaptureFrameEventArgs(CaptureFrame frame) : EventArgs
{
    /// <summary>
    /// The frame remains valid only for the duration of the event callback.
    /// Consumers that need to retain it must copy the pixels.
    /// </summary>
    public CaptureFrame Frame { get; } = frame;
}

public sealed class CaptureErrorEventArgs(string message, Exception? exception = null) : EventArgs
{
    public string Message { get; } = message;

    public Exception? Exception { get; } = exception;
}

public interface IWindowCaptureService : IAsyncDisposable
{
    event EventHandler<CaptureFrameEventArgs>? FrameArrived;

    event EventHandler<CaptureErrorEventArgs>? CaptureFailed;

    event EventHandler<double>? FramesPerSecondChanged;

    bool IsRunning { get; }

    IntPtr SourceWindow { get; }

    Task StartAsync(
        IntPtr sourceWindow,
        WindowCaptureOptions? options = null,
        CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
