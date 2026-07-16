using AstroDesk.Core.Entities;
using AstroDesk.Core.Enums;
using AstroDesk.Core.Exceptions;
using AstroDesk.Core.Models;

namespace AstroDesk.Core.Tests;

public sealed class ShootingSessionTests
{
    [Fact]
    public void Lifecycle_ExcludesPausedTimeFromActualDuration()
    {
        var session = CreateSession();
        var start = session.CreatedAt.AddMinutes(1);

        session.Start(start);
        session.Pause(start.AddMinutes(10));
        session.Resume(start.AddMinutes(15));
        session.End(start.AddMinutes(30), batteryPercentageAtEnd: 65, storageBytesAtEnd: 900);

        Assert.Equal(SessionStatus.Completed, session.Status);
        Assert.Equal(TimeSpan.FromMinutes(5), session.TotalPausedDuration);
        Assert.Equal(TimeSpan.FromMinutes(25), session.GetActualSessionDuration());
        Assert.Equal(65, session.BatteryPercentageAtEnd);
        Assert.Equal(900, session.StorageBytesAtEnd);
    }

    [Fact]
    public void End_WhilePaused_ExcludesOpenPause()
    {
        var session = CreateSession();
        var start = session.CreatedAt.AddMinutes(1);

        session.Start(start);
        session.Pause(start.AddMinutes(5));
        session.End(start.AddMinutes(12));

        Assert.Equal(TimeSpan.FromMinutes(5), session.GetActualSessionDuration());
        Assert.Null(session.PausedAt);
    }

    [Fact]
    public void InvalidLifecycleTransition_ThrowsDomainValidationException()
    {
        var session = CreateSession();

        Assert.Throws<DomainValidationException>(() => session.Pause(session.CreatedAt.AddMinutes(1)));
        Assert.Throws<DomainValidationException>(() => session.End(session.CreatedAt.AddMinutes(1)));
    }

    [Fact]
    public void Frames_UpdateProgressAndIntegration()
    {
        var session = CreateSession();
        var start = session.CreatedAt.AddSeconds(1);
        session.Start(start);

        session.RecordFrames(42, start.AddMinutes(1));

        Assert.Equal(42, session.FrameCount);
        Assert.Equal(78, session.RemainingFrames);
        Assert.Equal(35, session.ProgressPercentage, precision: 6);
        Assert.Equal(TimeSpan.FromMinutes(14), session.TotalIntegrationTime);

        session.CorrectFrames(2, start.AddMinutes(2));
        Assert.Equal(40, session.FrameCount);
    }

    [Fact]
    public void RecordFrames_RequiresActiveSession()
    {
        var session = CreateSession();

        Assert.Throws<DomainValidationException>(() => session.RecordFrames());
    }

    [Fact]
    public void Constructor_RejectsInvalidCoordinates()
    {
        var request = CreateRequest() with { Latitude = 91 };

        Assert.Throws<ArgumentOutOfRangeException>(() => new ShootingSession(request));
    }

    private static ShootingSession CreateSession() => new(CreateRequest());

    internal static CreateSessionRequest CreateRequest() =>
        new(
            TargetName: "Milky Way core",
            SessionType: SessionType.MilkyWay,
            LocationName: "Faraya",
            Latitude: 34.0111,
            Longitude: 35.8285,
            ExposureTime: TimeSpan.FromSeconds(20),
            PlannedFrameCount: 120,
            Camera: "Samsung Galaxy S23 Ultra",
            SelectedPhoneLens: "1x",
            Iso: 1600,
            RawEnabled: true,
            BatteryPercentageAtStart: 90,
            StorageBytesAtStart: 1_000);
}
