using AstroDesk.Core.Enums;

namespace AstroDesk.Core.Models;

public sealed record CreateSessionRequest(
    string TargetName,
    SessionType SessionType,
    string LocationName,
    double Latitude,
    double Longitude,
    TimeSpan ExposureTime,
    int PlannedFrameCount,
    TimeSpan? DelayBetweenFrames = null,
    TimeSpan? InitialDelay = null,
    string? Camera = null,
    string? SelectedPhoneLens = null,
    int? Iso = null,
    string? WhiteBalance = null,
    string? FocusSetting = null,
    bool RawEnabled = false,
    int? BatteryPercentageAtStart = null,
    long? StorageBytesAtStart = null,
    Guid? EquipmentProfileId = null);

public sealed record SessionSearchCriteria(
    string? SearchText = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    string? TargetName = null,
    string? LocationName = null,
    SessionType? SessionType = null,
    int? MinimumRating = null,
    int Skip = 0,
    int Take = 100);
