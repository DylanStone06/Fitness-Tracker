using NodaTime;

namespace FitnessTracker.Web.Tests.Infrastructure;

/// <summary>A clock that always says the same moment, so tests that depend on "today" don't change as real time passes.</summary>
public sealed class FixedClock(Instant now) : IClock
{
    /// <summary>Midday on Monday 19 October 2026 (UTC): "today" for every integration test.</summary>
    public static FixedClock TestsToday { get; } = new(Instant.FromUtc(2026, 10, 19, 12, 0));

    public Instant GetCurrentInstant() => now;
}
