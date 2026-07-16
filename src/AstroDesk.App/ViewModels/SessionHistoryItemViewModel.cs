using System.Globalization;
using AstroDesk.Core.Entities;

namespace AstroDesk.App.ViewModels;

public sealed class SessionHistoryItemViewModel(ShootingSession session)
{
    public ShootingSession Session { get; } = session;

    public Guid Id => Session.Id;

    public string Target => Session.TargetName;

    public string Location => Session.LocationName;

    public string Type => SplitPascalCase(Session.SessionType.ToString());

    public string DateText => (Session.StartTime ?? Session.CreatedAt)
        .ToLocalTime()
        .ToString("dd MMM yyyy  HH:mm", CultureInfo.CurrentCulture);

    public string DurationText => FormatDuration(Session.ActualSessionDuration);

    public string FramesText => $"{Session.FrameCount} / {Session.PlannedFrameCount}";

    public string IntegrationText => FormatDuration(Session.TotalIntegrationTime);

    public string RatingText => Session.Rating is { } rating
        ? new string('★', rating) + new string('☆', 5 - rating)
        : "Not rated";

    public string StatusText => Session.Status.ToString();

    public string Summary =>
        $"{Type} · {Location} · {Session.FrameCount} frames · {IntegrationText} integration";

    public string ConditionsSummary
    {
        get
        {
            string weather = Session.WeatherSnapshot is null
                ? "Weather unavailable"
                : $"{Format(Session.WeatherSnapshot.TemperatureCelsius, "0.0", "°C")}, " +
                  $"{Format(Session.WeatherSnapshot.CloudCoverPercent, "0", "% clouds")}, " +
                  $"{Session.WeatherSnapshot.DewRisk} dew risk";
            string moon = Session.AstronomySnapshot is null
                ? "Moon unavailable"
                : $"{Session.AstronomySnapshot.MoonPhase ?? "Moon phase unavailable"}, " +
                  $"{Format(Session.AstronomySnapshot.MoonIlluminationPercent, "0", "%")}";
            return $"{weather} · {moon}";
        }
    }

    public string SettingsSummary =>
        $"ISO {Session.Iso?.ToString(CultureInfo.InvariantCulture) ?? "Unavailable"} · " +
        $"{Session.ExposureTime.TotalSeconds:0.###} s · " +
        $"{(string.IsNullOrWhiteSpace(Session.SelectedPhoneLens) ? "Lens unavailable" : Session.SelectedPhoneLens)} · " +
        $"RAW {(Session.RawEnabled ? "on" : "off")}";

    public string BatterySummary =>
        Session.BatteryPercentageAtStart is null && Session.BatteryPercentageAtEnd is null
            ? "Unavailable"
            : $"{Session.BatteryPercentageAtStart?.ToString(CultureInfo.InvariantCulture) ?? "?"}% → " +
              $"{Session.BatteryPercentageAtEnd?.ToString(CultureInfo.InvariantCulture) ?? "?"}%";

    public string StorageSummary =>
        Session.StorageBytesAtStart is null && Session.StorageBytesAtEnd is null
            ? "Unavailable"
            : $"{FormatBytes(Session.StorageBytesAtStart)} → {FormatBytes(Session.StorageBytesAtEnd)}";

    public string NotesText => Session.Notes.Count == 0
        ? "No notes."
        : string.Join(
            Environment.NewLine + Environment.NewLine,
            Session.Notes
                .OrderBy(note => note.NotedAt)
                .Select(note => $"{note.NotedAt.ToLocalTime():HH:mm} · {note.Kind}{Environment.NewLine}{note.Content}"));

    public IReadOnlyList<string> ScreenshotPaths => Session.Screenshots
        .OrderBy(screenshot => screenshot.CapturedAt)
        .Select(screenshot => screenshot.FilePath)
        .ToArray();

    private static string SplitPascalCase(string value) =>
        string.Concat(
            value.Select(
                (character, index) =>
                    index > 0 && char.IsUpper(character) && !char.IsUpper(value[index - 1])
                        ? $" {character}"
                        : character.ToString()));

    private static string FormatDuration(TimeSpan value) =>
        value.TotalHours >= 1
            ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
            : $"{value.Minutes}:{value.Seconds:00}";

    private static string Format(double? value, string format, string suffix) =>
        value is null
            ? "Unavailable"
            : value.Value.ToString(format, CultureInfo.CurrentCulture) + suffix;

    private static string FormatBytes(long? bytes)
    {
        if (bytes is null)
        {
            return "Unavailable";
        }

        double value = bytes.Value;
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }
}
