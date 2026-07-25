using AstroDesk.Core.Enums;

namespace AstroDesk.Core.Models;

/// <summary>
/// Something worth pointing a phone at, with the coordinates needed to work out
/// when it is up.
/// </summary>
/// <param name="RightAscensionHours">J2000 right ascension, 0 to 24.</param>
/// <param name="DeclinationDegrees">J2000 declination, -90 to 90.</param>
public sealed record CelestialTarget(
    string Name,
    double RightAscensionHours,
    double DeclinationDegrees,
    SessionType SessionType,
    string Notes);

/// <summary>
/// The targets a phone on a fixed tripod can actually record.
/// </summary>
/// <remarks>
/// Deliberately short. A full catalogue would mostly list objects that need
/// tracking, aperture, or both, and burying the handful of reachable targets
/// among thousands of unreachable ones makes the tab worse rather than better.
/// Everything here is big and bright enough to show up in stacked exposures of a
/// few seconds at wide field.
/// </remarks>
public static class CelestialTargets
{
    public static IReadOnlyList<CelestialTarget> Reachable { get; } =
    [
        new("Milky Way core", 17.76, -28.94, SessionType.MilkyWay,
            "Brightest part of the galaxy, in Sagittarius. Needs a genuinely dark sky."),
        new("Andromeda (M31)", 0.712, 41.27, SessionType.DeepSky,
            "The largest galaxy in the sky, wider than the full Moon."),
        new("Orion Nebula (M42)", 5.588, -5.39, SessionType.DeepSky,
            "The brightest nebula. Survives a compromised sky better than most."),
        new("Pleiades (M45)", 3.79, 24.12, SessionType.DeepSky,
            "Bright open cluster, easy at wide field; the blue haze needs dark skies."),
        new("North America Nebula", 20.98, 44.52, SessionType.DeepSky,
            "Large emission nebula in Cygnus. Rewards long total exposure."),
        new("Double Cluster", 2.33, 57.14, SessionType.Constellation,
            "Two open clusters in Perseus, circumpolar from mid-northern latitudes."),
    ];
}

/// <summary>
/// When a target is usable tonight, and what is standing in the way.
/// </summary>
public sealed record TargetPlan(
    CelestialTarget Target,
    DateTimeOffset? WindowStart,
    DateTimeOffset? WindowEnd,
    double PeakAltitudeDegrees,
    DateTimeOffset? PeakTime,
    double MoonSeparationDegrees,
    bool MoonIsUpDuringWindow,
    double MoonIlluminationPercent)
{
    /// <summary>
    /// True when the target clears the horizon usefully during the dark window.
    /// </summary>
    public bool IsUsable => WindowStart is not null && WindowEnd is not null;

    public TimeSpan Duration =>
        IsUsable ? WindowEnd!.Value - WindowStart!.Value : TimeSpan.Zero;

    /// <summary>
    /// How badly the Moon spoils this target tonight, 0 to 1.
    /// </summary>
    /// <remarks>
    /// A bright Moon close to the target is far worse than a bright Moon on the
    /// other side of the sky, and a Moon below the horizon does not matter at
    /// all however full it is. Separation and illumination therefore multiply
    /// rather than being averaged.
    /// </remarks>
    public double MoonPenalty
    {
        get
        {
            if (!MoonIsUpDuringWindow || MoonIlluminationPercent <= 0)
            {
                return 0;
            }

            // Beyond about 90 degrees the Moon is a sky-brightness problem
            // rather than a glare problem, so the curve flattens there.
            double proximity = 1 - (Math.Clamp(MoonSeparationDegrees, 0, 90) / 90);
            return Math.Clamp(MoonIlluminationPercent / 100 * ((proximity * 0.7) + 0.3), 0, 1);
        }
    }
}
