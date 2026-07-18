using AstroDesk.Device.Adb;
using AstroDesk.Stacking;
using Xunit;

namespace AstroDesk.Stacking.Tests;

/// <summary>
/// Case 2 and 4: one exposure must yield one completion signal, and everything
/// else in the camera folder must be ignored.
/// </summary>
public sealed class CaptureCorrelatorTests
{
    private static readonly DateTimeOffset Pressed =
        new(2026, 7, 18, 22, 0, 0, TimeSpan.FromHours(3));

    [Fact]
    public void RawPlusJpeg_CountsAsOneCapture()
    {
        // Samsung writes 20260718_220005.jpg and .dng for a single press, and the
        // larger DNG can land a poll later. Counting both advances the run twice
        // for one exposure and desynchronises it permanently.
        var correlator = new CaptureCorrelator();
        correlator.ShutterPressed(Pressed);

        Assert.Equal(
            CaptureMatch.NewCapture,
            correlator.Classify("20260718_220005.jpg", Pressed.AddSeconds(21)));

        Assert.Equal(
            CaptureMatch.DuplicateOfCountedCapture,
            correlator.Classify("20260718_220005.dng", Pressed.AddSeconds(23)));

        Assert.Equal(1, correlator.CapturesCounted);
    }

    [Fact]
    public void DngArrivingFirst_StillCountsOnce()
    {
        var correlator = new CaptureCorrelator();
        correlator.ShutterPressed(Pressed);

        Assert.Equal(
            CaptureMatch.NewCapture,
            correlator.Classify("shot_001.dng", Pressed.AddSeconds(20)));
        Assert.Equal(
            CaptureMatch.DuplicateOfCountedCapture,
            correlator.Classify("shot_001.jpg", Pressed.AddSeconds(20)));
    }

    [Fact]
    public void SameFileDeliveredTwice_IsADuplicate()
    {
        // A duplicate sync event must not advance the run.
        var correlator = new CaptureCorrelator();
        correlator.ShutterPressed(Pressed);

        correlator.Classify("a.jpg", Pressed.AddSeconds(20));
        Assert.Equal(
            CaptureMatch.DuplicateOfCountedCapture,
            correlator.Classify("a.jpg", Pressed.AddSeconds(20)));
    }

    [Fact]
    public void FilesOlderThanTheShutter_DoNotCount()
    {
        // The camera folder already holds 1,400 old shots; none of them complete
        // a press issued tonight.
        var correlator = new CaptureCorrelator();
        correlator.ShutterPressed(Pressed);

        Assert.Equal(
            CaptureMatch.PredatesShutter,
            correlator.Classify("20260717_222137.jpg", Pressed.AddDays(-1)));
        Assert.Equal(0, correlator.CapturesCounted);
    }

    [Fact]
    public void NothingCountsWithoutAnOutstandingPress()
    {
        var correlator = new CaptureCorrelator();
        Assert.Equal(
            CaptureMatch.NoPendingShutter,
            correlator.Classify("x.jpg", Pressed));
    }

    [Fact]
    public void OnlyOneCaptureCountsPerPress()
    {
        // Two genuinely different captures cannot both satisfy one press.
        var correlator = new CaptureCorrelator();
        correlator.ShutterPressed(Pressed);

        Assert.Equal(CaptureMatch.NewCapture, correlator.Classify("a.jpg", Pressed.AddSeconds(20)));
        Assert.Equal(CaptureMatch.NoPendingShutter, correlator.Classify("b.jpg", Pressed.AddSeconds(21)));
    }

    [Theory]
    [InlineData("clip.mp4")]                    // 245 of these sit in DCIM/Camera
    [InlineData(".pending-1-20260718.jpg")]     // MediaStore placeholder
    [InlineData("20260718_220005.jpg.tmp")]     // partially written
    [InlineData("20260718_220005.jpg.part")]
    [InlineData(".thumbnails/thumb_1.jpg")]     // thumbnail
    [InlineData(".trashed-1-photo.jpg")]        // pending delete
    [InlineData("notes.txt")]
    public void NonCaptureFilesAreRejected(string path) =>
        Assert.False(CaptureCorrelator.IsCaptureFile(path));

    [Theory]
    [InlineData("20260718_220005.jpg")]
    [InlineData("20260718_220005.dng")]
    [InlineData("IMG_0001.HEIC")]
    public void RealCapturesAreAccepted(string path) =>
        Assert.True(CaptureCorrelator.IsCaptureFile(path));

    [Fact]
    public void ClockSkewWithinToleranceStillCounts()
    {
        // The phone's clock is not the laptop's, and the timestamp may be taken
        // when the exposure began rather than when the file closed.
        var correlator = new CaptureCorrelator();
        correlator.ShutterPressed(Pressed);

        Assert.Equal(
            CaptureMatch.NewCapture,
            correlator.Classify("a.jpg", Pressed.AddSeconds(-30)));
    }

    [Fact]
    public void Reset_ClearsCountedStems()
    {
        var correlator = new CaptureCorrelator();
        correlator.ShutterPressed(Pressed);
        correlator.Classify("a.jpg", Pressed.AddSeconds(1));

        correlator.Reset();
        correlator.ShutterPressed(Pressed);

        // The same filename in a new run is a new capture, not a duplicate.
        Assert.Equal(CaptureMatch.NewCapture, correlator.Classify("a.jpg", Pressed.AddSeconds(1)));
    }
}

/// <summary>
/// Cases 3, 5, 6 and 7 at the service level.
/// </summary>
public sealed class AutoCaptureHardeningTests
{
    [Fact]
    public async Task RawPlusJpeg_DoesNotDoubleTriggerTheNextShutter()
    {
        // The regression this hardening exists for: before correlation, the DNG
        // landing after its JPEG released the signal a second time and fired the
        // next shutter mid-exposure, where the press is silently discarded.
        var input = new RecordingInputService();
        await using var service = new AutoCaptureService(input);

        await service.StartAsync(
            new AutoCaptureOptions("S", TimeSpan.Zero, TimeSpan.Zero, FrameCount: 4));
        await input.WaitForPressesAsync(1);

        DateTimeOffset now = DateTimeOffset.Now;
        Assert.Equal(CaptureMatch.NewCapture, service.NotifyFileImported("20260718_1.jpg", now));
        Assert.Equal(
            CaptureMatch.DuplicateOfCountedCapture,
            service.NotifyFileImported("20260718_1.dng", now.AddSeconds(2)));

        await input.WaitForPressesAsync(2);
        await Task.Delay(500);

        // Exactly one further press, not two.
        Assert.Equal(2, input.PressCount);
        await service.StopAsync();
    }

    [Fact]
    public async Task StaleFilesDoNotAdvanceTheRun()
    {
        var input = new RecordingInputService();
        await using var service = new AutoCaptureService(input);

        await service.StartAsync(
            new AutoCaptureOptions("S", TimeSpan.Zero, TimeSpan.Zero, FrameCount: 3));
        await input.WaitForPressesAsync(1);

        service.NotifyFileImported("old.jpg", DateTimeOffset.Now.AddDays(-1));
        service.NotifyFileImported("clip.mp4", DateTimeOffset.Now);
        service.NotifyFileImported(".pending-1-x.jpg", DateTimeOffset.Now);

        await Task.Delay(500);
        Assert.Equal(1, input.PressCount);

        await service.StopAsync();
    }

    [Fact]
    public async Task StoppingDuringSettle_PreventsAnyFurtherShutter()
    {
        // Case 5: the settle delay is a window in which a stop must still be
        // honoured, or the phone receives a press after the user stopped.
        var input = new RecordingInputService();
        await using var service = new AutoCaptureService(input);

        await service.StartAsync(
            new AutoCaptureOptions("S", TimeSpan.Zero, TimeSpan.FromSeconds(8), FrameCount: 5));
        await input.WaitForPressesAsync(1);

        service.NotifyFileImported("a.jpg", DateTimeOffset.Now);

        // Settle is 2s for an 8s write delay; stop inside it.
        await Task.Delay(200);
        await service.StopAsync();

        int atStop = input.PressCount;
        await Task.Delay(3000);

        Assert.Equal(atStop, input.PressCount);
        Assert.False(service.IsRunning);
    }

    [Fact]
    public async Task AFrameIsNotReportedStartedWhenTheShutterCommandFails()
    {
        // Case 6: a failed ADB command means no exposure began, so nothing may
        // be reported as triggered.
        var input = new FailingInputService();
        await using var service = new AutoCaptureService(input);
        var finished = new TaskCompletionSource<AutoCaptureFinished>();
        service.Finished += (_, args) => finished.TrySetResult(args);
        int triggered = 0;
        service.Triggered += (_, _) => Interlocked.Increment(ref triggered);

        await service.StartAsync(
            new AutoCaptureOptions("S", TimeSpan.Zero, TimeSpan.Zero, FrameCount: 3));

        AutoCaptureFinished result = await finished.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(0, triggered);
        Assert.Equal(0, result.FramesTriggered);
        Assert.Equal(1, result.Telemetry.ShutterCommandsFailed);
        Assert.Equal(0, result.Telemetry.ShutterCommandsSent);
    }

    [Fact]
    public async Task TelemetryAccountsForEveryFileAndPress()
    {
        // Case 7: the numbers must reconcile, because every failure here is quiet.
        var input = new RecordingInputService();
        await using var service = new AutoCaptureService(input);

        // Three requested, so press 2 is still outstanding when the stale file is
        // offered. With only two the run would have already finished and the
        // file would be rejected as "not running" before reaching the correlator.
        await service.StartAsync(
            new AutoCaptureOptions("S", TimeSpan.Zero, TimeSpan.Zero, FrameCount: 3));
        await input.WaitForPressesAsync(1);

        DateTimeOffset now = DateTimeOffset.Now;
        service.NotifyFileImported("a.jpg", now);          // completes press 1
        service.NotifyFileImported("a.dng", now);          // RAW rendition of the same shot
        service.NotifyFileImported("clip.mp4", now);       // one of the 245 videos in the folder

        // Wait until press 2 is genuinely outstanding before offering the stale
        // file, otherwise it is classified as "nothing pending" rather than
        // "predates the shutter" and the assertion would race the run loop.
        await input.WaitForPressesAsync(2);
        service.NotifyFileImported("ancient.jpg", now.AddDays(-2));

        await service.StopAsync();

        AutoCaptureTelemetry t = service.Telemetry;
        Assert.Equal(3, t.ShotsRequested);
        Assert.Equal(2, t.ShutterCommandsSent);
        Assert.Equal(1, t.CapturesCounted);
        Assert.Equal(1, t.DuplicatesIgnored);
        Assert.Equal(1, t.IgnoredNotCapture);
        Assert.Equal(1, t.IgnoredPredatingShutter);

        // One press outstanding at stop: sent 2, counted 1.
        Assert.Equal(1, t.UnaccountedShutters);
    }

    [Fact]
    public async Task FilesArrivingAfterStopAreRejected()
    {
        var input = new RecordingInputService();
        await using var service = new AutoCaptureService(input);

        await service.StartAsync(
            new AutoCaptureOptions("S", TimeSpan.Zero, TimeSpan.Zero, FrameCount: 3));
        await input.WaitForPressesAsync(1);
        await service.StopAsync();

        Assert.Equal(
            CaptureMatch.NoPendingShutter,
            service.NotifyFileImported("late.jpg", DateTimeOffset.Now));
    }

    [Fact]
    public async Task ANewRunDoesNotInheritThepreviousRunsSignals()
    {
        // Case 3: a capture from a finished session must not advance a new one.
        var input = new RecordingInputService();
        await using var service = new AutoCaptureService(input);

        await service.StartAsync(
            new AutoCaptureOptions("S", TimeSpan.Zero, TimeSpan.Zero, FrameCount: 2));
        await input.WaitForPressesAsync(1);
        await service.StopAsync();

        input.Reset();
        await service.StartAsync(
            new AutoCaptureOptions("S", TimeSpan.Zero, TimeSpan.Zero, FrameCount: 2));
        await input.WaitForPressesAsync(1);
        await Task.Delay(400);

        // Only the new run's opening press; no carry-over advance.
        Assert.Equal(1, input.PressCount);
        await service.StopAsync();
    }

    private sealed class RecordingInputService : IAdbInputService
    {
        private readonly SemaphoreSlim _pressed = new(0);
        private int _pressCount;

        public int PressCount => Volatile.Read(ref _pressCount);

        public void Reset() => Interlocked.Exchange(ref _pressCount, 0);

        public async Task WaitForPressesAsync(int count)
        {
            while (PressCount < count)
            {
                if (!await _pressed.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
                {
                    throw new TimeoutException($"only saw {PressCount} of {count} presses");
                }
            }
        }

        public Task SendKeyAsync(string serial, AndroidKeyCode keyCode, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _pressCount);
            _pressed.Release();
            return Task.CompletedTask;
        }

        public Task TapAsync(string serial, int x, int y, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task SwipeAsync(
            string serial, int startX, int startY, int endX, int endY,
            TimeSpan duration, CancellationToken ct = default) => Task.CompletedTask;

        public Task SendTextAsync(string serial, string text, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task SetKeepAwakeAsync(string serial, bool enabled, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class FailingInputService : IAdbInputService
    {
        public Task SendKeyAsync(string serial, AndroidKeyCode keyCode, CancellationToken ct = default) =>
            throw new InvalidOperationException("adb: device offline");

        public Task TapAsync(string serial, int x, int y, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task SwipeAsync(
            string serial, int startX, int startY, int endX, int endY,
            TimeSpan duration, CancellationToken ct = default) => Task.CompletedTask;

        public Task SendTextAsync(string serial, string text, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task SetKeepAwakeAsync(string serial, bool enabled, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
