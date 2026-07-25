using AstroDesk.Core.Enums;
using AstroDesk.Core.Models;

namespace AstroDesk.App.ViewModels;

/// <summary>
/// One target on the Plan tab, formatted for reading at a glance in the dark.
/// </summary>
/// <remarks>
/// Times are rendered here rather than by converters in the view because they
/// have to be shown in the site's own time zone, which the view has no way to
/// know.
/// </remarks>
public sealed class TargetPlanRow(TargetPlan plan, Func<DateTimeOffset?, string> formatTime)
{
    public string Name => plan.Target.Name;

    public string Notes => plan.Target.Notes;

    public SessionType SessionType => plan.Target.SessionType;

    public bool IsUsable => plan.IsUsable;

    public string WindowText => plan.IsUsable
        ? $"{formatTime(plan.WindowStart)} – {formatTime(plan.WindowEnd)}"
        : "Not high enough tonight";

    public string DurationText => plan.IsUsable
        ? $"{plan.Duration.TotalHours:0.#} h above {TargetPlannerAltitude}°"
        : $"Peaks at {plan.PeakAltitudeDegrees:0}°";

    public string PeakText => plan.PeakTime is null
        ? "—"
        : $"{plan.PeakAltitudeDegrees:0}° at {formatTime(plan.PeakTime)}";

    /// <summary>
    /// Says what the Moon is doing rather than only how bright it is: a full
    /// Moon that has set is not a problem, and a thin one beside the target can
    /// still be.
    /// </summary>
    public string MoonText => !plan.MoonIsUpDuringWindow
        ? "Moon down — clear run"
        : $"Moon {plan.MoonIlluminationPercent:0}% at {plan.MoonSeparationDegrees:0}° away";

    public bool IsMoonProblem => plan.MoonPenalty >= 0.35;

    /// <summary>
    /// A short verdict, because a row of numbers still needs reading.
    /// </summary>
    public string Verdict => !plan.IsUsable
        ? "Skip tonight"
        : plan.MoonPenalty switch
        {
            >= 0.6 => "Washed out by the Moon",
            >= 0.35 => "Usable, but the Moon will cost you",
            _ when plan.PeakAltitudeDegrees >= 55 => "Excellent — high and clear",
            _ => "Worth shooting",
        };

    private static double TargetPlannerAltitude =>
        AstroDesk.Infrastructure.Providers.TargetPlanner.MinimumUsableAltitudeDegrees;
}
