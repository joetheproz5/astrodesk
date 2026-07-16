using System.Globalization;
using System.Text;
using System.Text.Json;
using AstroDesk.Core.Entities;

namespace AstroDesk.Infrastructure.Storage;

public interface ISessionAssetService
{
    string GetOrCreateSessionFolder(ShootingSession session);

    string GetOrCreateScreenshotFolder(ShootingSession session);

    Task<string> ExportJsonAsync(
        ShootingSession session,
        CancellationToken cancellationToken = default);

    Task<string> ExportMarkdownAsync(
        ShootingSession session,
        CancellationToken cancellationToken = default);

    Task WritePortableFilesAsync(
        ShootingSession session,
        CancellationToken cancellationToken = default);
}

public sealed class SessionAssetService(AppPaths paths) : ISessionAssetService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly AppPaths _paths = paths;

    public string GetOrCreateSessionFolder(ShootingSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        string date = (session.StartTime ?? session.CreatedAt).ToLocalTime().ToString(
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture);
        string type = Slugify(SplitPascalCase(session.SessionType.ToString()));
        string location = Slugify(session.LocationName);
        string target = Slugify(session.TargetName);
        string descriptiveName = string.Join(
            "_",
            new[]
            {
                date,
                type,
                target,
                location,
                session.Id.ToString("N", CultureInfo.InvariantCulture)[..8],
            }.Where(static value => value.Length > 0));
        string folder = Path.Combine(_paths.SessionRoot, descriptiveName);
        Directory.CreateDirectory(folder);
        Directory.CreateDirectory(Path.Combine(folder, "screenshots"));
        Directory.CreateDirectory(Path.Combine(folder, "export"));
        return folder;
    }

    public string GetOrCreateScreenshotFolder(ShootingSession session) =>
        Path.Combine(GetOrCreateSessionFolder(session), "screenshots");

    public async Task<string> ExportJsonAsync(
        ShootingSession session,
        CancellationToken cancellationToken = default)
    {
        string folder = GetOrCreateSessionFolder(session);
        string path = Path.Combine(folder, "export", "session.json");
        string json = JsonSerializer.Serialize(CreateExportModel(session), JsonOptions);
        await File.WriteAllTextAsync(path, json, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        return path;
    }

    public async Task<string> ExportMarkdownAsync(
        ShootingSession session,
        CancellationToken cancellationToken = default)
    {
        string folder = GetOrCreateSessionFolder(session);
        string path = Path.Combine(folder, "export", "summary.md");
        string markdown = CreateMarkdown(session);
        await File.WriteAllTextAsync(path, markdown, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        return path;
    }

    public async Task WritePortableFilesAsync(
        ShootingSession session,
        CancellationToken cancellationToken = default)
    {
        string folder = GetOrCreateSessionFolder(session);
        string sessionJsonPath = Path.Combine(folder, "session.json");
        string notesPath = Path.Combine(folder, "notes.txt");
        string json = JsonSerializer.Serialize(CreateExportModel(session), JsonOptions);
        string notes = string.Join(
            Environment.NewLine + Environment.NewLine,
            session.Notes
                .OrderBy(note => note.NotedAt)
                .Select(note => $"[{note.NotedAt:O}] {note.Kind}{Environment.NewLine}{note.Content}"));

        await Task.WhenAll(
                File.WriteAllTextAsync(sessionJsonPath, json, Encoding.UTF8, cancellationToken),
                File.WriteAllTextAsync(notesPath, notes, Encoding.UTF8, cancellationToken))
            .ConfigureAwait(false);
    }

    private static object CreateExportModel(ShootingSession session) => new
    {
        session.Id,
        session.CreatedAt,
        session.UpdatedAt,
        session.StartTime,
        session.EndTime,
        Status = session.Status.ToString(),
        SessionType = SplitPascalCase(session.SessionType.ToString()),
        session.TargetName,
        session.LocationName,
        session.Latitude,
        session.Longitude,
        session.Camera,
        session.SelectedPhoneLens,
        session.Iso,
        ExposureSeconds = session.ExposureTime.TotalSeconds,
        DelayBetweenFramesSeconds = session.DelayBetweenFrames.TotalSeconds,
        InitialDelaySeconds = session.InitialDelay.TotalSeconds,
        session.WhiteBalance,
        session.FocusSetting,
        session.RawEnabled,
        session.FrameCount,
        session.PlannedFrameCount,
        PlannedIntegrationSeconds = session.PlannedIntegrationTime.TotalSeconds,
        TotalIntegrationSeconds = session.TotalIntegrationTime.TotalSeconds,
        ActualDurationSeconds = session.ActualSessionDuration.TotalSeconds,
        session.Problems,
        session.Rating,
        session.BatteryPercentageAtStart,
        session.BatteryPercentageAtEnd,
        session.StorageBytesAtStart,
        session.StorageBytesAtEnd,
        Weather = session.WeatherSnapshot is null
            ? null
            : new
            {
                session.WeatherSnapshot.CapturedAt,
                session.WeatherSnapshot.TemperatureCelsius,
                session.WeatherSnapshot.HumidityPercent,
                session.WeatherSnapshot.WindSpeedKilometersPerHour,
                session.WeatherSnapshot.CloudCoverPercent,
                session.WeatherSnapshot.VisibilityKilometers,
                session.WeatherSnapshot.DewPointCelsius,
                DewRisk = session.WeatherSnapshot.DewRisk.ToString(),
                session.WeatherSnapshot.ProviderName,
            },
        Astronomy = session.AstronomySnapshot is null
            ? null
            : new
            {
                session.AstronomySnapshot.CapturedAt,
                session.AstronomySnapshot.Sunset,
                session.AstronomySnapshot.EndOfAstronomicalTwilight,
                session.AstronomySnapshot.Sunrise,
                session.AstronomySnapshot.StartOfAstronomicalTwilight,
                session.AstronomySnapshot.MoonPhase,
                session.AstronomySnapshot.MoonIlluminationPercent,
                session.AstronomySnapshot.Moonrise,
                session.AstronomySnapshot.Moonset,
                session.AstronomySnapshot.MoonAltitudeDegrees,
                session.AstronomySnapshot.DarkSkyWindowStart,
                session.AstronomySnapshot.DarkSkyWindowEnd,
                session.AstronomySnapshot.ProviderName,
            },
        Notes = session.Notes
            .OrderBy(note => note.NotedAt)
            .Select(note => new { note.Id, note.NotedAt, Kind = note.Kind.ToString(), note.Content }),
        Screenshots = session.Screenshots
            .OrderBy(screenshot => screenshot.CapturedAt)
            .Select(screenshot => new
            {
                screenshot.Id,
                screenshot.CapturedAt,
                screenshot.FilePath,
                screenshot.IncludesOverlays,
                screenshot.ImageFormat,
            }),
    };

    private static string CreateMarkdown(ShootingSession session)
    {
        StringBuilder builder = new();
        builder.AppendLine($"# {session.TargetName}");
        builder.AppendLine();
        builder.AppendLine($"- Date: {(session.StartTime ?? session.CreatedAt):yyyy-MM-dd HH:mm zzz}");
        builder.AppendLine($"- Type: {SplitPascalCase(session.SessionType.ToString())}");
        builder.AppendLine($"- Location: {session.LocationName} ({session.Latitude:0.######}, {session.Longitude:0.######})");
        builder.AppendLine($"- Status: {session.Status}");
        builder.AppendLine($"- Duration: {FormatDuration(session.ActualSessionDuration)}");
        builder.AppendLine($"- Frames: {session.FrameCount} / {session.PlannedFrameCount}");
        builder.AppendLine($"- Exposure: {session.ExposureTime.TotalSeconds:0.###} seconds");
        builder.AppendLine($"- Total integration: {FormatDuration(session.TotalIntegrationTime)}");
        builder.AppendLine($"- Camera / lens: {Value(session.Camera)} / {Value(session.SelectedPhoneLens)}");
        builder.AppendLine($"- ISO: {(session.Iso?.ToString(CultureInfo.InvariantCulture) ?? "Unavailable")}");
        builder.AppendLine($"- RAW: {(session.RawEnabled ? "Enabled" : "Disabled")}");
        builder.AppendLine();
        builder.AppendLine("## Conditions");
        builder.AppendLine();

        if (session.WeatherSnapshot is { } weather)
        {
            builder.AppendLine($"- Temperature: {Format(weather.TemperatureCelsius, "0.0", " °C")}");
            builder.AppendLine($"- Humidity: {Format(weather.HumidityPercent, "0", "%")}");
            builder.AppendLine($"- Cloud cover: {Format(weather.CloudCoverPercent, "0", "%")}");
            builder.AppendLine($"- Wind: {Format(weather.WindSpeedKilometersPerHour, "0.0", " km/h")}");
            builder.AppendLine($"- Dew point: {Format(weather.DewPointCelsius, "0.0", " °C")} ({weather.DewRisk})");
        }
        else
        {
            builder.AppendLine("- Weather: Unavailable");
        }

        if (session.AstronomySnapshot is { } astronomy)
        {
            builder.AppendLine($"- Moon: {Value(astronomy.MoonPhase)}, {Format(astronomy.MoonIlluminationPercent, "0", "%")}");
            builder.AppendLine($"- Astronomical twilight ends: {FormatTime(astronomy.EndOfAstronomicalTwilight)}");
            builder.AppendLine($"- Astronomical twilight starts: {FormatTime(astronomy.StartOfAstronomicalTwilight)}");
        }
        else
        {
            builder.AppendLine("- Astronomy: Unavailable");
        }

        builder.AppendLine();
        builder.AppendLine("## Notes");
        builder.AppendLine();
        if (session.Notes.Count == 0)
        {
            builder.AppendLine("No notes.");
        }
        else
        {
            foreach (SessionNote note in session.Notes.OrderBy(note => note.NotedAt))
            {
                builder.AppendLine($"### {note.Kind} — {note.NotedAt:HH:mm:ss}");
                builder.AppendLine();
                builder.AppendLine(note.Content);
                builder.AppendLine();
            }
        }

        if (!string.IsNullOrWhiteSpace(session.Problems))
        {
            builder.AppendLine("## Problems");
            builder.AppendLine();
            builder.AppendLine(session.Problems);
            builder.AppendLine();
        }

        builder.AppendLine("## Preview screenshots");
        builder.AppendLine();
        builder.AppendLine(
            session.Screenshots.Count == 0
                ? "No preview screenshots."
                : $"{session.Screenshots.Count} preview screenshot(s). These are captured from the mirrored preview, not full-resolution phone photographs.");
        return builder.ToString();
    }

    private static string Slugify(string value)
    {
        StringBuilder builder = new();
        bool previousDash = false;
        foreach (char character in value.Trim())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousDash = false;
            }
            else if (!previousDash)
            {
                builder.Append('-');
                previousDash = true;
            }
        }

        return builder.ToString().Trim('-');
    }

    private static string SplitPascalCase(string value)
    {
        StringBuilder builder = new(value.Length + 8);
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (index > 0 && char.IsUpper(character) && !char.IsUpper(value[index - 1]))
            {
                builder.Append(' ');
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    private static string FormatDuration(TimeSpan value) =>
        value.TotalHours >= 1
            ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
            : $"{value.Minutes}:{value.Seconds:00}";

    private static string Value(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "Unavailable" : value;

    private static string Format(double? value, string pattern, string suffix) =>
        value is null
            ? "Unavailable"
            : value.Value.ToString(pattern, CultureInfo.InvariantCulture) + suffix;

    private static string FormatTime(DateTimeOffset? value) =>
        value?.ToLocalTime().ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.CurrentCulture)
        ?? "Unavailable";
}
