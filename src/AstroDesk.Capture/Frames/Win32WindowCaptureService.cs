using System.Buffers;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace AstroDesk.Capture.Frames;

/// <summary>
/// Captures the client area of a top-level window using PrintWindow with a BitBlt fallback.
/// This backend is intentionally abstracted so Windows Graphics Capture can be introduced
/// without changing preview, histogram, screenshot, or input-mapping consumers.
/// </summary>
public sealed partial class Win32WindowCaptureService(
    ILogger<Win32WindowCaptureService> logger) : IWindowCaptureService
{
    private readonly ILogger<Win32WindowCaptureService> _logger = logger;
    private readonly SemaphoreSlim _stateGate = new(1, 1);
    private CancellationTokenSource? _captureCancellation;
    private Task? _captureTask;
    private Task? _dispatchTask;
    private Channel<CaptureFrame>? _frames;

    public event EventHandler<CaptureFrameEventArgs>? FrameArrived;

    public event EventHandler<CaptureErrorEventArgs>? CaptureFailed;

    public event EventHandler<double>? FramesPerSecondChanged;

    public bool IsRunning => _captureTask is { IsCompleted: false };

    public IntPtr SourceWindow { get; private set; }

    public async Task StartAsync(
        IntPtr sourceWindow,
        WindowCaptureOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (sourceWindow == IntPtr.Zero)
        {
            throw new ArgumentException("A valid source window handle is required.", nameof(sourceWindow));
        }

        if (!NativeMethods.IsWindow(sourceWindow))
        {
            throw new ArgumentException("The source window handle is not valid.", nameof(sourceWindow));
        }

        options ??= new WindowCaptureOptions();
        if (options.FramesPerSecond is < 1 or > 120)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Capture frame rate must be between 1 and 120 FPS.");
        }

        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);

            SourceWindow = sourceWindow;
            _captureCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _frames = Channel.CreateBounded<CaptureFrame>(
                new BoundedChannelOptions(2)
                {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = true,
                    AllowSynchronousContinuations = false,
                });

            CancellationToken token = _captureCancellation.Token;
            _captureTask = Task.Run(
                () => CaptureLoopAsync(sourceWindow, options, _frames, token),
                CancellationToken.None);
            _dispatchTask = Task.Run(
                () => DispatchLoopAsync(_frames.Reader, token),
                CancellationToken.None);
        }
        finally
        {
            _stateGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _stateGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _stateGate.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task StopCoreAsync()
    {
        CancellationTokenSource? cancellation = _captureCancellation;
        Task? capture = _captureTask;
        Task? dispatch = _dispatchTask;

        _captureCancellation = null;
        _captureTask = null;
        _dispatchTask = null;
        SourceWindow = IntPtr.Zero;

        if (cancellation is null)
        {
            return;
        }

        cancellation.Cancel();
        _frames?.Writer.TryComplete();

        try
        {
            await Task.WhenAll(
                    capture ?? Task.CompletedTask,
                    dispatch ?? Task.CompletedTask)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cancellation.Dispose();

            if (_frames is not null)
            {
                while (_frames.Reader.TryRead(out CaptureFrame? frame))
                {
                    frame.Dispose();
                }
            }

            _frames = null;
        }
    }

    private async Task CaptureLoopAsync(
        IntPtr sourceWindow,
        WindowCaptureOptions options,
        Channel<CaptureFrame> channel,
        CancellationToken cancellationToken)
    {
        TimeSpan frameInterval = TimeSpan.FromSeconds(1d / options.FramesPerSecond);
        PeriodicTimer timer = new(frameInterval);
        Stopwatch fpsClock = Stopwatch.StartNew();
        int framesThisInterval = 0;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                CaptureFrame frame = Capture(sourceWindow, options.PreferPrintWindow);
                framesThisInterval++;

                while (!channel.Writer.TryWrite(frame))
                {
                    if (channel.Reader.TryRead(out CaptureFrame? oldFrame))
                    {
                        oldFrame.Dispose();
                        continue;
                    }

                    await channel.Writer.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
                    break;
                }

                if (fpsClock.Elapsed >= TimeSpan.FromSeconds(1))
                {
                    double fps = framesThisInterval / fpsClock.Elapsed.TotalSeconds;
                    framesThisInterval = 0;
                    fpsClock.Restart();
                    FramesPerSecondChanged?.Invoke(this, fps);
                }

                if (!await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            const string message = "The embedded phone preview capture stopped unexpectedly.";
            _logger.LogError(exception, "{Message}", message);
            CaptureFailed?.Invoke(this, new CaptureErrorEventArgs(message, exception));
        }
        finally
        {
            timer.Dispose();
            channel.Writer.TryComplete();
        }
    }

    private async Task DispatchLoopAsync(
        ChannelReader<CaptureFrame> reader,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (CaptureFrame frame in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                using (frame)
                {
                    FrameArrived?.Invoke(this, new CaptureFrameEventArgs(frame));
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static CaptureFrame Capture(IntPtr sourceWindow, bool preferPrintWindow)
    {
        if (!NativeMethods.GetClientRect(sourceWindow, out NativeMethods.Rect rect))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not read the scrcpy client size.");
        }

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException("The scrcpy client area has no capturable size.");
        }

        IntPtr windowDc = NativeMethods.GetDC(sourceWindow);
        if (windowDc == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not acquire the scrcpy window DC.");
        }

        IntPtr memoryDc = IntPtr.Zero;
        IntPtr bitmap = IntPtr.Zero;
        IntPtr previous = IntPtr.Zero;
        byte[]? rented = null;

        try
        {
            memoryDc = NativeMethods.CreateCompatibleDC(windowDc);
            bitmap = NativeMethods.CreateCompatibleBitmap(windowDc, width, height);
            if (memoryDc == IntPtr.Zero || bitmap == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create capture surfaces.");
            }

            previous = NativeMethods.SelectObject(memoryDc, bitmap);
            bool rendered = preferPrintWindow &&
                            NativeMethods.PrintWindow(
                                sourceWindow,
                                memoryDc,
                                NativeMethods.PrintWindowClientOnly | NativeMethods.PrintWindowRenderFullContent);

            if (!rendered)
            {
                rendered = NativeMethods.BitBlt(
                    memoryDc,
                    0,
                    0,
                    width,
                    height,
                    windowDc,
                    0,
                    0,
                    NativeMethods.SourceCopy | NativeMethods.CaptureBlt);
            }

            if (!rendered)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not render the scrcpy window.");
            }

            int stride = checked(width * 4);
            int length = checked(stride * height);
            rented = ArrayPool<byte>.Shared.Rent(length);

            NativeMethods.BitmapInfo info = NativeMethods.BitmapInfo.Create(width, height);
            int scanLines = NativeMethods.GetDIBits(
                memoryDc,
                bitmap,
                0,
                (uint)height,
                rented,
                ref info,
                NativeMethods.DibRgbColors);

            if (scanLines == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not read captured scrcpy pixels.");
            }

            CaptureFrame frame = new(
                rented,
                length,
                width,
                height,
                stride,
                DateTimeOffset.UtcNow);
            rented = null;
            return frame;
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }

            if (previous != IntPtr.Zero && memoryDc != IntPtr.Zero)
            {
                _ = NativeMethods.SelectObject(memoryDc, previous);
            }

            if (bitmap != IntPtr.Zero)
            {
                _ = NativeMethods.DeleteObject(bitmap);
            }

            if (memoryDc != IntPtr.Zero)
            {
                _ = NativeMethods.DeleteDC(memoryDc);
            }

            _ = NativeMethods.ReleaseDC(sourceWindow, windowDc);
        }
    }

    private static partial class NativeMethods
    {
        public const uint SourceCopy = 0x00CC0020;
        public const uint CaptureBlt = 0x40000000;
        public const uint DibRgbColors = 0;
        public const uint PrintWindowClientOnly = 0x00000001;
        public const uint PrintWindowRenderFullContent = 0x00000002;

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool IsWindow(IntPtr window);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool GetClientRect(IntPtr window, out Rect rect);

        [LibraryImport("user32.dll", SetLastError = true)]
        public static partial IntPtr GetDC(IntPtr window);

        [LibraryImport("user32.dll", SetLastError = true)]
        public static partial int ReleaseDC(IntPtr window, IntPtr dc);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool PrintWindow(IntPtr window, IntPtr targetDc, uint flags);

        [LibraryImport("gdi32.dll", SetLastError = true)]
        public static partial IntPtr CreateCompatibleDC(IntPtr dc);

        [LibraryImport("gdi32.dll", SetLastError = true)]
        public static partial IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);

        [LibraryImport("gdi32.dll", SetLastError = true)]
        public static partial IntPtr SelectObject(IntPtr dc, IntPtr value);

        [LibraryImport("gdi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool DeleteObject(IntPtr value);

        [LibraryImport("gdi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool DeleteDC(IntPtr dc);

        [LibraryImport("gdi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool BitBlt(
            IntPtr targetDc,
            int targetX,
            int targetY,
            int width,
            int height,
            IntPtr sourceDc,
            int sourceX,
            int sourceY,
            uint rasterOperation);

        [LibraryImport("gdi32.dll", SetLastError = true)]
        public static partial int GetDIBits(
            IntPtr dc,
            IntPtr bitmap,
            uint startScan,
            uint scanLines,
            [Out] byte[] bits,
            ref BitmapInfo bitmapInfo,
            uint usage);

        [StructLayout(LayoutKind.Sequential)]
        public struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BitmapInfoHeader
        {
            public uint Size;
            public int Width;
            public int Height;
            public ushort Planes;
            public ushort BitCount;
            public uint Compression;
            public uint SizeImage;
            public int XPelsPerMeter;
            public int YPelsPerMeter;
            public uint ColorsUsed;
            public uint ColorsImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BitmapInfo
        {
            public BitmapInfoHeader Header;
            public uint Color;

            public static BitmapInfo Create(int width, int height) => new()
            {
                Header = new BitmapInfoHeader
                {
                    Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                    Width = width,
                    Height = -height,
                    Planes = 1,
                    BitCount = 32,
                    Compression = 0,
                    SizeImage = (uint)checked(width * height * 4),
                },
            };
        }
    }
}
