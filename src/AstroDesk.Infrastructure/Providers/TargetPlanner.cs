using AstroDesk.Core.Models;
using CosineKitty;

namespace AstroDesk.Infrastructure.Providers;

/// <summary>
/// Works out which targets are usable tonight, and what is in the way.
/// </summary>
/// <remarks>
/// The score already answers "is tonight worth going out" and the light
/// pollution map answers "where do I go". This answers the question that comes
/// before either: what is actually above the horizon while the sky is dark, and
/// when is it highest.
/// </remarks>
public static class TargetPlanner
{
    /// <summary>
    /// Altitude below which a wide-field phone shot is not worth taking.
    /// </summary>
    /// <remarks>
    /// Near the horizon you are shooting through several times as much
    /// atmosphere, and that is also where the town glow lives. Twenty-five
    /// degrees is the usual practical floor; below it detail is lost to
    /// extinction no matter how long the total exposure.
    /// </remarks>
    public const double MinimumUsableAltitudeDegrees = 25;

    /// <summary>
    /// How finely the night is sampled when searching for the window.
    /// </summary>
    /// <remarks>
    /// The sky turns a degree every four minutes, so ten-minute steps place the
    /// window to within a few degrees of altitude - far finer than the decision
    /// it informs, and cheap enough to recompute whenever the site changes.
    /// </remarks>
    private static readonly TimeSpan SampleInterval = TimeSpan.FromMinutes(10);

    public static IReadOnlyList<TargetPlan> PlanNight(
        IEnumerable<CelestialTarget> targets,
        double latitude,
        double longitude,
        DateTimeOffset darkStart,
        DateTimeOffset darkEnd)
    {
        ArgumentNullException.ThrowIfNull(targets);

        if (darkEnd <= darkStart)
        {
            return [];
        }

        Observer observer = new(latitude, longitude, 0);
        List<TargetPlan> plans = [];

        foreach (CelestialTarget target in targets)
        {
            plans.Add(Plan(target, observer, darkStart, darkEnd));
        }

        // Best first: a usable target beats an unusable one, then the one that is
        // highest, since altitude is what decides how much atmosphere is in the way.
        return
        [
            .. plans
                .OrderByDescending(plan => plan.IsUsable)
                .ThenByDescending(plan => plan.PeakAltitudeDegrees - (plan.MoonPenalty * 40)),
        ];
    }

    private static TargetPlan Plan(
        CelestialTarget target,
        Observer observer,
        DateTimeOffset darkStart,
        DateTimeOffset darkEnd)
    {
        DateTimeOffset? windowStart = null;
        DateTimeOffset? windowEnd = null;
        double peakAltitude = double.MinValue;
        DateTimeOffset? peakTime = null;
        double separationAtPeak = 180;
        bool moonUp = false;
        double moonIllumination = 0;

        for (DateTimeOffset time = darkStart; time <= darkEnd; time += SampleInterval)
        {
            AstroTime astroTime = new(time.UtcDateTime);
            double altitude = AltitudeOf(target, observer, astroTime);

            if (altitude >= MinimumUsableAltitudeDegrees)
            {
                windowStart ??= time;
                windowEnd = time;

                Topocentric moon = MoonPosition(observer, astroTime);
                if (moon.altitude > 0)
                {
                    moonUp = true;
                }
            }

            if (altitude > peakAltitude)
            {
                peakAltitude = altitude;
                peakTime = time;
                separationAtPeak = SeparationFromMoon(target, observer, astroTime);
                moonIllumination = Astronomy.Illumination(Body.Moon, astroTime).phase_fraction * 100;
            }
        }

        return new TargetPlan(
            target,
            windowStart,
            windowEnd,
            peakAltitude is double.MinValue ? 0 : peakAltitude,
            peakTime,
            separationAtPeak,
            moonUp,
            moonIllumination);
    }

    private static double AltitudeOf(CelestialTarget target, Observer observer, AstroTime time) =>
        Astronomy.Horizon(
            time,
            observer,
            target.RightAscensionHours,
            target.DeclinationDegrees,
            Refraction.Normal).altitude;

    private static Topocentric MoonPosition(Observer observer, AstroTime time)
    {
        Equatorial moon = Astronomy.Equator(
            Body.Moon,
            time,
            observer,
            EquatorEpoch.OfDate,
            Aberration.Corrected);
        return Astronomy.Horizon(time, observer, moon.ra, moon.dec, Refraction.Normal);
    }

    /// <summary>
    /// Angle between the target and the Moon, which is what decides whether
    /// moonlight is glare in the frame or just a brighter sky.
    /// </summary>
    private static double SeparationFromMoon(
        CelestialTarget target,
        Observer observer,
        AstroTime time)
    {
        Equatorial moon = Astronomy.Equator(
            Body.Moon,
            time,
            observer,
            EquatorEpoch.OfDate,
            Aberration.Corrected);

        double targetRa = target.RightAscensionHours * 15 * Math.PI / 180;
        double targetDec = target.DeclinationDegrees * Math.PI / 180;
        double moonRa = moon.ra * 15 * Math.PI / 180;
        double moonDec = moon.dec * Math.PI / 180;

        double cosine =
            (Math.Sin(targetDec) * Math.Sin(moonDec)) +
            (Math.Cos(targetDec) * Math.Cos(moonDec) * Math.Cos(targetRa - moonRa));

        return Math.Acos(Math.Clamp(cosine, -1, 1)) * 180 / Math.PI;
    }
}
