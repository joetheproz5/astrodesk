using AstroDesk.Core.Calculations;
using AstroDesk.Core.Enums;
using AstroDesk.Core.Exceptions;
using AstroDesk.Core.Models;

namespace AstroDesk.Core.Entities;

public sealed class ShootingSession : EntityBase
{
    private readonly List<SessionScreenshot> _screenshots = [];
    private readonly List<SessionNote> _notes = [];

    private ShootingSession()
    {
        TargetName = string.Empty;
        LocationName = string.Empty;
        Camera = string.Empty;
        SelectedPhoneLens = string.Empty;
        WhiteBalance = string.Empty;
        FocusSetting = string.Empty;
    }

    public ShootingSession(CreateSessionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        TargetName = RequiredText(request.TargetName, nameof(request.TargetName), 200);
        LocationName = RequiredText(request.LocationName, nameof(request.LocationName), 200);
        ValidateCoordinates(request.Latitude, request.Longitude);
        ValidateDuration(request.ExposureTime, nameof(request.ExposureTime), allowZero: false);
        ValidateDuration(request.DelayBetweenFrames ?? TimeSpan.Zero, nameof(request.DelayBetweenFrames), allowZero: true);
        ValidateDuration(request.InitialDelay ?? TimeSpan.Zero, nameof(request.InitialDelay), allowZero: true);

        if (request.PlannedFrameCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request.PlannedFrameCount),
                "Planned frame count cannot be negative.");
        }

        ValidatePercentage(request.BatteryPercentageAtStart, nameof(request.BatteryPercentageAtStart));
        ValidateNonNegative(request.StorageBytesAtStart, nameof(request.StorageBytesAtStart));
        if (request.Iso is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.Iso), "ISO must be greater than zero.");
        }

        SessionType = request.SessionType;
        Latitude = request.Latitude;
        Longitude = request.Longitude;
        ExposureTime = request.ExposureTime;
        PlannedFrameCount = request.PlannedFrameCount;
        DelayBetweenFrames = request.DelayBetweenFrames ?? TimeSpan.Zero;
        InitialDelay = request.InitialDelay ?? TimeSpan.Zero;
        Camera = OptionalText(request.Camera, nameof(request.Camera), 200) ?? string.Empty;
        SelectedPhoneLens = OptionalText(request.SelectedPhoneLens, nameof(request.SelectedPhoneLens), 100) ?? string.Empty;
        Iso = request.Iso;
        WhiteBalance = OptionalText(request.WhiteBalance, nameof(request.WhiteBalance), 100) ?? string.Empty;
        FocusSetting = OptionalText(request.FocusSetting, nameof(request.FocusSetting), 100) ?? string.Empty;
        RawEnabled = request.RawEnabled;
        BatteryPercentageAtStart = request.BatteryPercentageAtStart;
        StorageBytesAtStart = request.StorageBytesAtStart;
        EquipmentProfileId = request.EquipmentProfileId;
        Status = SessionStatus.Planned;
    }

    public string TargetName { get; private set; }

    public SessionType SessionType { get; private set; }

    public SessionStatus Status { get; private set; }

    public DateTimeOffset? StartTime { get; private set; }

    public DateTimeOffset? EndTime { get; private set; }

    public DateTimeOffset? PausedAt { get; private set; }

    public TimeSpan TotalPausedDuration { get; private set; }

    public string LocationName { get; private set; }

    public double Latitude { get; private set; }

    public double Longitude { get; private set; }

    public string Camera { get; private set; }

    public string SelectedPhoneLens { get; private set; }

    public int? Iso { get; private set; }

    public TimeSpan ExposureTime { get; private set; }

    public TimeSpan DelayBetweenFrames { get; private set; }

    public TimeSpan InitialDelay { get; private set; }

    public string WhiteBalance { get; private set; }

    public string FocusSetting { get; private set; }

    public bool RawEnabled { get; private set; }

    public int FrameCount { get; private set; }

    public int PlannedFrameCount { get; private set; }

    public string? Problems { get; private set; }

    public int? Rating { get; private set; }

    public int? BatteryPercentageAtStart { get; private set; }

    public int? BatteryPercentageAtEnd { get; private set; }

    public long? StorageBytesAtStart { get; private set; }

    public long? StorageBytesAtEnd { get; private set; }

    public Guid? EquipmentProfileId { get; private set; }

    public EquipmentProfile? EquipmentProfile { get; private set; }

    public SessionWeatherSnapshot? WeatherSnapshot { get; private set; }

    public SessionAstronomySnapshot? AstronomySnapshot { get; private set; }

    public IReadOnlyCollection<SessionScreenshot> Screenshots => _screenshots.AsReadOnly();

    public IReadOnlyCollection<SessionNote> Notes => _notes.AsReadOnly();

    public TimeSpan PlannedIntegrationTime =>
        ExposureCalculations.CalculateIntegrationTime(ExposureTime, PlannedFrameCount);

    public TimeSpan TotalIntegrationTime =>
        ExposureCalculations.CalculateIntegrationTime(ExposureTime, FrameCount);

    public int RemainingFrames =>
        ExposureCalculations.CalculateRemainingFrames(PlannedFrameCount, FrameCount);

    public double ProgressPercentage =>
        ExposureCalculations.CalculateProgressPercentage(PlannedFrameCount, FrameCount);

    public TimeSpan EstimatedRemainingCaptureTime =>
        ExposureCalculations.EstimateRemainingCaptureTime(
            ExposureTime,
            DelayBetweenFrames,
            PlannedFrameCount,
            FrameCount);

    public TimeSpan ActualSessionDuration => GetActualSessionDuration();

    public void Start(DateTimeOffset timestamp)
    {
        EnsureStatus(SessionStatus.Planned, "Only a planned session can be started.");
        EnsureTimestampNotBeforeCreation(timestamp);
        StartTime = timestamp;
        Status = SessionStatus.Active;
        MarkUpdated(timestamp);
    }

    public void Pause(DateTimeOffset timestamp)
    {
        EnsureStatus(SessionStatus.Active, "Only an active session can be paused.");
        EnsureTimestampAtOrAfterStart(timestamp);
        PausedAt = timestamp;
        Status = SessionStatus.Paused;
        MarkUpdated(timestamp);
    }

    public void Resume(DateTimeOffset timestamp)
    {
        EnsureStatus(SessionStatus.Paused, "Only a paused session can be resumed.");
        if (PausedAt is null || timestamp < PausedAt.Value)
        {
            throw new DomainValidationException("Resume time cannot precede the pause time.");
        }

        TotalPausedDuration += timestamp - PausedAt.Value;
        PausedAt = null;
        Status = SessionStatus.Active;
        MarkUpdated(timestamp);
    }

    public void End(
        DateTimeOffset timestamp,
        int? batteryPercentageAtEnd = null,
        long? storageBytesAtEnd = null)
    {
        if (Status is not (SessionStatus.Active or SessionStatus.Paused))
        {
            throw new DomainValidationException("Only an active or paused session can be ended.");
        }

        EnsureTimestampAtOrAfterStart(timestamp);
        ValidatePercentage(batteryPercentageAtEnd, nameof(batteryPercentageAtEnd));
        ValidateNonNegative(storageBytesAtEnd, nameof(storageBytesAtEnd));

        if (Status == SessionStatus.Paused)
        {
            if (PausedAt is null || timestamp < PausedAt.Value)
            {
                throw new DomainValidationException("End time cannot precede the pause time.");
            }

            TotalPausedDuration += timestamp - PausedAt.Value;
            PausedAt = null;
        }

        EndTime = timestamp;
        BatteryPercentageAtEnd = batteryPercentageAtEnd;
        StorageBytesAtEnd = storageBytesAtEnd;
        Status = SessionStatus.Completed;
        MarkUpdated(timestamp);
    }

    public TimeSpan GetActualSessionDuration(DateTimeOffset? asOf = null)
    {
        if (StartTime is null)
        {
            return TimeSpan.Zero;
        }

        var effectiveEnd = EndTime ?? asOf ?? DateTimeOffset.UtcNow;
        if (effectiveEnd < StartTime.Value)
        {
            throw new ArgumentOutOfRangeException(nameof(asOf), "Duration timestamp cannot precede session start.");
        }

        var activePause = PausedAt is { } pausedAt
            ? effectiveEnd - pausedAt
            : TimeSpan.Zero;
        var duration = effectiveEnd - StartTime.Value - TotalPausedDuration - activePause;
        return duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
    }

    public void RecordFrames(int count = 1, DateTimeOffset? timestamp = null)
    {
        EnsureStatus(SessionStatus.Active, "Frames can only be recorded while a session is active.");
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Frame increment must be greater than zero.");
        }

        FrameCount = checked(FrameCount + count);
        MarkUpdated(timestamp);
    }

    public void CorrectFrames(int count = 1, DateTimeOffset? timestamp = null)
    {
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Frame correction must be greater than zero.");
        }

        if (count > FrameCount)
        {
            throw new DomainValidationException("Frame count cannot be negative.");
        }

        FrameCount -= count;
        MarkUpdated(timestamp);
    }

    public void UpdatePlan(
        TimeSpan exposureTime,
        int plannedFrameCount,
        TimeSpan? delayBetweenFrames = null,
        TimeSpan? initialDelay = null,
        DateTimeOffset? timestamp = null)
    {
        EnsureNotCompleted();
        ValidateDuration(exposureTime, nameof(exposureTime), allowZero: false);
        ValidateDuration(delayBetweenFrames ?? TimeSpan.Zero, nameof(delayBetweenFrames), allowZero: true);
        ValidateDuration(initialDelay ?? TimeSpan.Zero, nameof(initialDelay), allowZero: true);
        if (plannedFrameCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(plannedFrameCount),
                "Planned frame count cannot be negative.");
        }

        ExposureTime = exposureTime;
        PlannedFrameCount = plannedFrameCount;
        DelayBetweenFrames = delayBetweenFrames ?? TimeSpan.Zero;
        InitialDelay = initialDelay ?? TimeSpan.Zero;
        MarkUpdated(timestamp);
    }

    public void UpdateCaptureSettings(
        string? camera,
        string? selectedPhoneLens,
        int? iso,
        string? whiteBalance,
        string? focusSetting,
        bool rawEnabled,
        DateTimeOffset? timestamp = null)
    {
        EnsureNotCompleted();
        if (iso is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(iso), "ISO must be greater than zero.");
        }

        Camera = OptionalText(camera, nameof(camera), 200) ?? string.Empty;
        SelectedPhoneLens = OptionalText(selectedPhoneLens, nameof(selectedPhoneLens), 100) ?? string.Empty;
        Iso = iso;
        WhiteBalance = OptionalText(whiteBalance, nameof(whiteBalance), 100) ?? string.Empty;
        FocusSetting = OptionalText(focusSetting, nameof(focusSetting), 100) ?? string.Empty;
        RawEnabled = rawEnabled;
        MarkUpdated(timestamp);
    }

    public void SetOutcome(string? problems, int? rating, DateTimeOffset? timestamp = null)
    {
        if (rating is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(rating), "Rating must be between 1 and 5.");
        }

        Problems = OptionalText(problems, nameof(problems), 4000);
        Rating = rating;
        MarkUpdated(timestamp);
    }

    public void SetWeatherSnapshot(SessionWeatherSnapshot snapshot, DateTimeOffset? timestamp = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        snapshot.AttachTo(this);
        WeatherSnapshot = snapshot;
        MarkUpdated(timestamp);
    }

    public void SetAstronomySnapshot(SessionAstronomySnapshot snapshot, DateTimeOffset? timestamp = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        snapshot.AttachTo(this);
        AstronomySnapshot = snapshot;
        MarkUpdated(timestamp);
    }

    public SessionScreenshot AddScreenshot(
        string filePath,
        bool includesOverlays,
        DateTimeOffset capturedAt,
        string imageFormat = "PNG")
    {
        var screenshot = new SessionScreenshot(Id, filePath, includesOverlays, capturedAt, imageFormat);
        _screenshots.Add(screenshot);
        MarkUpdated(capturedAt);
        return screenshot;
    }

    public SessionNote AddNote(
        string content,
        SessionNoteKind kind,
        DateTimeOffset timestamp)
    {
        var note = new SessionNote(Id, content, kind, timestamp);
        _notes.Add(note);
        MarkUpdated(timestamp);
        return note;
    }

    private void EnsureStatus(SessionStatus required, string message)
    {
        if (Status != required)
        {
            throw new DomainValidationException(message);
        }
    }

    private void EnsureNotCompleted()
    {
        if (Status == SessionStatus.Completed)
        {
            throw new DomainValidationException("A completed session cannot be changed.");
        }
    }

    private void EnsureTimestampNotBeforeCreation(DateTimeOffset timestamp)
    {
        if (timestamp < CreatedAt)
        {
            throw new DomainValidationException("Session start cannot precede its creation time.");
        }
    }

    private void EnsureTimestampAtOrAfterStart(DateTimeOffset timestamp)
    {
        if (StartTime is null || timestamp < StartTime.Value)
        {
            throw new DomainValidationException("Timestamp cannot precede the session start.");
        }
    }

    private static string RequiredText(string value, string paramName, int maxLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);
        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(paramName, $"Value cannot exceed {maxLength} characters.");
        }

        return trimmed;
    }

    private static string? OptionalText(string? value, string paramName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return RequiredText(value, paramName, maxLength);
    }

    private static void ValidateCoordinates(double latitude, double longitude)
    {
        if (!double.IsFinite(latitude) || latitude is < -90 or > 90)
        {
            throw new ArgumentOutOfRangeException(nameof(latitude), "Latitude must be between -90 and 90.");
        }

        if (!double.IsFinite(longitude) || longitude is < -180 or > 180)
        {
            throw new ArgumentOutOfRangeException(nameof(longitude), "Longitude must be between -180 and 180.");
        }
    }

    private static void ValidateDuration(TimeSpan value, string paramName, bool allowZero)
    {
        if (value < TimeSpan.Zero || (!allowZero && value == TimeSpan.Zero))
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                allowZero ? "Duration cannot be negative." : "Duration must be greater than zero.");
        }
    }

    private static void ValidatePercentage(int? value, string paramName)
    {
        if (value is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(paramName, "Percentage must be between 0 and 100.");
        }
    }

    private static void ValidateNonNegative(long? value, string paramName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(paramName, "Value cannot be negative.");
        }
    }
}
