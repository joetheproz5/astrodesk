using AstroDesk.Core.Models;
using AstroDesk.Infrastructure.Providers;
using Xunit;

namespace AstroDesk.Core.Tests;

/// <summary>
/// Covers working out which targets are actually up while the sky is dark.
/// </summary>
/// <remarks>
/// Assertions are pinned to facts that hold independently of this code - the
/// Milky Way core is a summer target from the northern hemisphere, Andromeda an
/// autumn one, and neither the sky nor the calendar is going to change to suit
/// the implementation.
/// </remarks>
public sealed class TargetPlannerTests
{
    // A mid-northern test site. The latitude is what the seasonal assertions
    // below depend on - the Milky Way core only just clears a usable altitude
    // from here in July, and not at all in January - so it cannot move far
    // without changing what the tests mean.
    private const double Latitude = 34.0;
    private const double Longitude = 35.0;

    private static CelestialTarget Find(string name) =>
        CelestialTargets.Reachable.Single(target => target.Name.Contains(name, StringComparison.OrdinalIgnoreCase));

    private static TargetPlan PlanFor(string name, DateTimeOffset darkStart, DateTimeOffset darkEnd) =>
        TargetPlanner.PlanNight([Find(name)], Latitude, Longitude, darkStart, darkEnd).Single();

    [Fact]
    public void TheMilkyWayCoreIsUpOnASummerNight()
    {
        // Mid-July, astronomical dark. The core is the reason people shoot in
        // summer from this latitude.
        TargetPlan plan = PlanFor(
            "Milky Way",
            new DateTimeOffset(2026, 7, 19, 21, 30, 0, TimeSpan.FromHours(3)),
            new DateTimeOffset(2026, 7, 20, 4, 0, 0, TimeSpan.FromHours(3)));

        Assert.True(plan.IsUsable, "the core should be usable on a July night from 33N");
        Assert.True(
            plan.PeakAltitudeDegrees > 25,
            $"peaked at only {plan.PeakAltitudeDegrees:0.#} degrees");
    }

    [Fact]
    public void TheMilkyWayCoreIsNotUpOnAWinterNight()
    {
        // In January the core is a daytime object from here, which is exactly the
        // sort of wasted drive this tab exists to prevent.
        TargetPlan plan = PlanFor(
            "Milky Way",
            new DateTimeOffset(2026, 1, 15, 19, 0, 0, TimeSpan.FromHours(2)),
            new DateTimeOffset(2026, 1, 16, 5, 0, 0, TimeSpan.FromHours(2)));

        Assert.False(plan.IsUsable, $"reported usable, peaking at {plan.PeakAltitudeDegrees:0.#} degrees");
    }

    [Fact]
    public void AndromedaIsAnAutumnTarget()
    {
        TargetPlan autumn = PlanFor(
            "Andromeda",
            new DateTimeOffset(2026, 10, 15, 20, 0, 0, TimeSpan.FromHours(3)),
            new DateTimeOffset(2026, 10, 16, 4, 0, 0, TimeSpan.FromHours(3)));

        Assert.True(autumn.IsUsable);
        Assert.True(autumn.PeakAltitudeDegrees > 60, $"was {autumn.PeakAltitudeDegrees:0.#}");
    }

    [Fact]
    public void APeakAltitudeNeverExceedsWhatTheLatitudeAllows()
    {
        // A target culminates at 90 - |latitude - declination|. Exceeding that
        // would mean the horizontal conversion is wrong.
        foreach (CelestialTarget target in CelestialTargets.Reachable)
        {
            TargetPlan plan = TargetPlanner.PlanNight(
                [target],
                Latitude,
                Longitude,
                new DateTimeOffset(2026, 7, 19, 18, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 7, 20, 18, 0, 0, TimeSpan.Zero)).Single();

            double highestPossible = 90 - Math.Abs(Latitude - target.DeclinationDegrees);

            // A degree of slack for atmospheric refraction lifting it slightly.
            Assert.True(
                plan.PeakAltitudeDegrees <= highestPossible + 1,
                $"{target.Name} peaked at {plan.PeakAltitudeDegrees:0.#}, above the {highestPossible:0.#} its declination allows");
        }
    }

    [Fact]
    public void AWindowFallsInsideTheDarkHoursItWasGiven()
    {
        DateTimeOffset darkStart = new(2026, 7, 19, 21, 30, 0, TimeSpan.FromHours(3));
        DateTimeOffset darkEnd = new(2026, 7, 20, 4, 0, 0, TimeSpan.FromHours(3));

        TargetPlan plan = PlanFor("Milky Way", darkStart, darkEnd);

        Assert.True(plan.WindowStart >= darkStart);
        Assert.True(plan.WindowEnd <= darkEnd);
        Assert.True(plan.Duration > TimeSpan.Zero);
    }

    [Fact]
    public void NothingIsPlannedWhenThereIsNoDarkness() =>
        Assert.Empty(TargetPlanner.PlanNight(
            CelestialTargets.Reachable,
            Latitude,
            Longitude,
            new DateTimeOffset(2026, 7, 19, 22, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 19, 22, 0, 0, TimeSpan.Zero)));

    [Fact]
    public void UsableTargetsAreListedBeforeUnusableOnes()
    {
        IReadOnlyList<TargetPlan> plans = TargetPlanner.PlanNight(
            CelestialTargets.Reachable,
            Latitude,
            Longitude,
            new DateTimeOffset(2026, 7, 19, 21, 30, 0, TimeSpan.FromHours(3)),
            new DateTimeOffset(2026, 7, 20, 4, 0, 0, TimeSpan.FromHours(3)));

        int lastUsable = plans.ToList().FindLastIndex(plan => plan.IsUsable);
        int firstUnusable = plans.ToList().FindIndex(plan => !plan.IsUsable);

        if (lastUsable >= 0 && firstUnusable >= 0)
        {
            Assert.True(lastUsable < firstUnusable, "an unusable target was listed above a usable one");
        }
    }

    [Fact]
    public void AMoonBelowTheHorizonCostsNothingHoweverFullItIs()
    {
        // The penalty has to key off whether the Moon is actually up. A full Moon
        // that has already set is not a problem, and treating it as one would send
        // people home on a perfectly good night.
        TargetPlan plan = new(
            Find("Andromeda"),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddHours(3),
            PeakAltitudeDegrees: 70,
            PeakTime: DateTimeOffset.UtcNow,
            MoonSeparationDegrees: 5,
            MoonIsUpDuringWindow: false,
            MoonIlluminationPercent: 100);

        Assert.Equal(0, plan.MoonPenalty);
    }

    [Fact]
    public void ABrightMoonBesideTheTargetCostsMoreThanOneAcrossTheSky()
    {
        TargetPlan close = MoonAt(separation: 10, illumination: 90);
        TargetPlan far = MoonAt(separation: 150, illumination: 90);

        Assert.True(
            close.MoonPenalty > far.MoonPenalty,
            $"close {close.MoonPenalty:0.00} should beat far {far.MoonPenalty:0.00}");
        Assert.True(far.MoonPenalty > 0, "a bright Moon still brightens the whole sky");
    }

    private static TargetPlan MoonAt(double separation, double illumination) =>
        new(
            Find("Andromeda"),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddHours(3),
            PeakAltitudeDegrees: 70,
            PeakTime: DateTimeOffset.UtcNow,
            MoonSeparationDegrees: separation,
            MoonIsUpDuringWindow: true,
            MoonIlluminationPercent: illumination);
}
