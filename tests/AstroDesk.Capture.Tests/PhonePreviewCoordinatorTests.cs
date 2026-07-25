using System.Buffers;
using System.Reflection;
using AstroDesk.App.Services;
using AstroDesk.Capture.Frames;
using AstroDesk.Capture.Histogram;
using AstroDesk.Capture.Screenshots;
using AstroDesk.Core.Enums;
using AstroDesk.Device.Scrcpy;
using Microsoft.Extensions.Logging.Abstractions;

namespace AstroDesk.Capture.Tests;

public sealed class PhonePreviewCoordinatorTests
{
    [Fact]
    public async Task DisplayModeChange_RetintsFrozenLatestFrameAndRestoresOriginal()
    {
        FakeWindowCaptureService capture = new();
        FakeScrcpyService scrcpy = new();
        using ThrottledHistogramProcessor histogram = new(
            new HistogramAnalyzer(),
            NullLogger<ThrottledHistogramProcessor>.Instance)
        {
            IsFrozen = true,
        };
        await using PhonePreviewCoordinator coordinator = new(
            capture,
            scrcpy,
            histogram,
            new PreviewScreenshotWriter(),
            new FakePhonePhotoSyncService(),
            new FakePhoneOrientationSessionService(),
            new FakeScrcpyWindowManager(),
            NullLogger<PhonePreviewCoordinator>.Instance);
        List<byte[]> renderedPixels = [];
        coordinator.FrameReady += (_, frame) =>
        {
            byte[] pixels = new byte[4];
            frame.Image.CopyPixels(pixels, 4, 0);
            renderedPixels.Add(pixels);
        };

        using CaptureFrame source = CreateFrame([10, 100, 200, 255]);
        capture.Emit(source);
        Assert.Equal([10, 100, 200, 255], Assert.Single(renderedPixels));

        coordinator.IsFrozen = true;
        coordinator.DisplayMode = NightDisplayMode.FullRed;

        // Full red dims the preview rather than tinting it, so hue survives and
        // only brightness drops: each channel scaled by 0.45, alpha untouched.
        Assert.Equal([5, 45, 90, 255], renderedPixels[1]);

        coordinator.DisplayMode = NightDisplayMode.NormalDark;
        Assert.Equal([10, 100, 200, 255], renderedPixels[2]);
    }

    private static CaptureFrame CreateFrame(byte[] pixels)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(pixels.Length);
        pixels.CopyTo(buffer, 0);
        ConstructorInfo constructor = typeof(CaptureFrame)
                                          .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
                                          .Single();
        return (CaptureFrame)constructor.Invoke(
            [buffer, pixels.Length, 1, 1, 4, DateTimeOffset.UtcNow]);
    }

    private sealed class FakeWindowCaptureService : IWindowCaptureService
    {
        public event EventHandler<CaptureFrameEventArgs>? FrameArrived;

        public event EventHandler<CaptureErrorEventArgs>? CaptureFailed
        {
            add { }
            remove { }
        }

        public event EventHandler<double>? FramesPerSecondChanged
        {
            add { }
            remove { }
        }

        public bool IsRunning { get; private set; }

        public IntPtr SourceWindow { get; private set; }

        public void Emit(CaptureFrame frame) =>
            FrameArrived?.Invoke(this, new CaptureFrameEventArgs(frame));

        public Task StartAsync(
            IntPtr sourceWindow,
            WindowCaptureOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            SourceWindow = sourceWindow;
            IsRunning = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            IsRunning = false;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeScrcpyService : IScrcpyService
    {
        public event EventHandler<ScrcpyStateChangedEventArgs>? StateChanged
        {
            add { }
            remove { }
        }

        public event EventHandler<ScrcpyLogEventArgs>? LogReceived
        {
            add { }
            remove { }
        }

        public event EventHandler<ScrcpyCrashedEventArgs>? Crashed
        {
            add { }
            remove { }
        }

        public ScrcpyState State { get; private set; }

        public ScrcpySession? CurrentSession { get; private set; }

        public Task<ScrcpySession> StartAsync(
            ScrcpyLaunchOptions options,
            CancellationToken cancellationToken = default)
        {
            State = ScrcpyState.Running;
            CurrentSession = CreateSession(options);
            return Task.FromResult(CurrentSession);
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            State = ScrcpyState.Stopped;
            CurrentSession = null;
            return Task.CompletedTask;
        }

        public Task<ScrcpySession> ReconnectAsync(CancellationToken cancellationToken = default) =>
            StartAsync(CurrentSession?.Options ?? new ScrcpyLaunchOptions(), cancellationToken);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private static ScrcpySession CreateSession(ScrcpyLaunchOptions options) =>
            new(
                Environment.ProcessId,
                new IntPtr(1),
                "AstroDesk-Test",
                DateTimeOffset.UtcNow,
                options);
    }

    private sealed class FakePhonePhotoSyncService : IPhonePhotoSyncService
    {
        public event EventHandler<PhonePhotoImportedEventArgs>? PhotoImported
        {
            add { }
            remove { }
        }

        public bool Enabled { get; set; }

        public string DestinationFolder { get; set; } = string.Empty;

        public string RemoteFolder { get; set; } = string.Empty;

        public Task StartAsync(string serial, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakePhoneOrientationSessionService : IPhoneOrientationSessionService
    {
        public Task EnterLandscapeAsync(string serial, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RestoreAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeScrcpyWindowManager : IScrcpyWindowManager
    {
        public Task<nint> FindWindowAsync(
            string exactTitle,
            int processId,
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(nint.Zero);

        public WindowPresentationState HideOffscreenWithoutMinimizing(nint windowHandle) =>
            new(windowHandle, default, nint.Zero);

        public void Restore(WindowPresentationState state)
        {
        }
    }

}
