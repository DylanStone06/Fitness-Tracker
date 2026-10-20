using FitnessTracker.Domain.Footprints;
using FitnessTracker.Domain.Scheduling;
using NodaTime;

namespace FitnessTracker.Domain.Tests.Scheduling;

// Requirements §7.3: daily load is the total footprint minutes on a day; §8: a footprint crossing midnight counts
// towards each day for the part on that day. Times are BST (UTC+1) in October 2026, written in UTC by hand.
public class DailyLoadTests
{
    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];

    private static readonly Guid Home = Guid.Parse("0199e3a0-0000-7000-8000-000000000001");
    private static readonly Guid Office = Guid.Parse("0199e3a0-0000-7000-8000-000000000002");
    private static readonly Guid Gym = Guid.Parse("0199e3a0-0000-7000-8000-000000000003");

    private static readonly TravelTimes Legs = new(new Dictionary<Leg, Duration>
    {
        [new Leg(Home, Office)] = Duration.FromMinutes(30),
        [new Leg(Office, Home)] = Duration.FromMinutes(30),
        [new Leg(Home, Gym)] = Duration.FromMinutes(25),
        [new Leg(Gym, Home)] = Duration.FromMinutes(45),
    });

    // T9: an office day with a 30 min commute each way, then an evening gym session from home. The commute is the
    // travel around work and the gym's travel is from home, so each is counted once: 60 + 480 + 120 = 660 min, and
    // the commute home (ends 17:30) doesn't clash with leaving for the gym (18:05).
    [Fact]
    public void AnOfficeDayAndAnEveningGymSession_CountTheCommuteAndTheGymTravelOnceEach()
    {
        // Monday 5 October 2026: work 09:00-17:00 BST, gym 18:30-19:20 BST.
        var work = new Booking("Work", Footprint.Around(
            new Interval(Instant.FromUtc(2026, 10, 5, 8, 0), Instant.FromUtc(2026, 10, 5, 16, 0)),
            Buffers.None, Legs.JourneysFor(activityAt: Office, baseLocation: Home).Journeys!.Value));
        var gym = new Booking("Gym", Footprint.Around(
            new Interval(Instant.FromUtc(2026, 10, 5, 17, 30), Instant.FromUtc(2026, 10, 5, 18, 20)),
            Buffers.None, Legs.JourneysFor(activityAt: Gym, baseLocation: Home).Journeys!.Value));

        var load = DailyLoad.Of([work.Footprint, gym.Footprint], London);

        Assert.Equal(new Dictionary<LocalDate, Duration> { [new LocalDate(2026, 10, 5)] = Duration.FromMinutes(660) }, load);
        Assert.Empty(Conflicts.Between(gym, [work]));
        // TRV-3: the commute is its own pair of blocks, 08:30-09:00 and 17:00-17:30 BST, either side of work.
        Assert.Equal([FootprintPart.TravelOut, FootprintPart.Activity, FootprintPart.TravelBack], work.Footprint.Segments.Select(part => part.Part));
        Assert.Equal(new Interval(Instant.FromUtc(2026, 10, 5, 7, 30), Instant.FromUtc(2026, 10, 5, 8, 0)), work.Footprint.Segments[0].Time);
        Assert.Equal(new Interval(Instant.FromUtc(2026, 10, 5, 16, 0), Instant.FromUtc(2026, 10, 5, 16, 30)), work.Footprint.Segments[2].Time);
    }

    // Known limitation (location chaining is after the MVP, SCH-8): each activity away from base gets a round trip
    // from base, so the office gym straight after work at the office gets a second commute. Pinned as built: one
    // clash with work, and 650 minutes rather than the 590 a shared commute would give. Listed in known limitations.
    [Fact]
    public void AnOfficeGymStraightAfterWork_GetsItsOwnCommute_UntilLocationChainingIsBuilt()
    {
        var commute = Legs.JourneysFor(activityAt: Office, baseLocation: Home).Journeys!.Value;
        var work = new Booking("Work", Footprint.Around(
            new Interval(Instant.FromUtc(2026, 10, 5, 8, 0), Instant.FromUtc(2026, 10, 5, 16, 0)), Buffers.None, commute));
        var officeGym = new Booking("Office gym", Footprint.Around(
            new Interval(Instant.FromUtc(2026, 10, 5, 16, 0), Instant.FromUtc(2026, 10, 5, 16, 50)), Buffers.None, commute));

        Assert.Single(Conflicts.Among([work, officeGym]));
        Assert.Equal(Duration.FromMinutes(650), DailyLoad.Of([work.Footprint, officeGym.Footprint], London)[new LocalDate(2026, 10, 5)]);
    }

    // Travel before an activity just after midnight counts towards the day before, so a day's load needs the
    // footprints of occurrences on the day after as well as the day before. Tuesday 6 October 2026, a run
    // 00:10-01:00 BST (23:10-00:00 UTC) with 25 min each way: 22:45-00:25 UTC, of which 15 min are before Monday ends
    // (23:00 UTC).
    [Fact]
    public void TravelBeforeAnActivityJustAfterMidnight_CountsTowardsTheDayBefore()
    {
        var run = Footprint.Around(
            new Interval(Instant.FromUtc(2026, 10, 5, 23, 10), Instant.FromUtc(2026, 10, 6, 0, 0)),
            Buffers.None, new Journeys(Out: Duration.FromMinutes(25), Back: Duration.FromMinutes(25)));

        Assert.Equal(
            new Dictionary<LocalDate, Duration>
            {
                [new LocalDate(2026, 10, 5)] = Duration.FromMinutes(15),
                [new LocalDate(2026, 10, 6)] = Duration.FromMinutes(85),
            },
            DailyLoad.Of([run], London));
    }

    // §8: the hour the clocks go back counts towards Sunday. Saturday 24 October 2026 23:30 BST (22:30 UTC) to the
    // second 01:30 on Sunday (01:30 UTC) is 180 min; midnight is 23:00 UTC, so Saturday 30 and Sunday 150.
    [Fact]
    public void OnTheNightTheClocksGoBack_TheRepeatedHourCountsTowardsSunday()
    {
        var late = Footprint.Around(
            new Interval(Instant.FromUtc(2026, 10, 24, 22, 30), Instant.FromUtc(2026, 10, 25, 1, 30)), Buffers.None, Journeys.None);

        Assert.Equal(
            new Dictionary<LocalDate, Duration>
            {
                [new LocalDate(2026, 10, 24)] = Duration.FromMinutes(30),
                [new LocalDate(2026, 10, 25)] = Duration.FromMinutes(150),
            },
            DailyLoad.Of([late], London));
    }

    // §8: going forward, Saturday 27 March 2027 23:00 GMT (23:00 UTC) to Sunday 03:00 BST (02:00 UTC) is 180 min;
    // midnight is 00:00 UTC, so Saturday 60 and Sunday 120.
    [Fact]
    public void OnTheNightTheClocksGoForward_TheLoadIsSplitAtTheRealMidnight()
    {
        var late = Footprint.Around(
            new Interval(Instant.FromUtc(2027, 3, 27, 23, 0), Instant.FromUtc(2027, 3, 28, 2, 0)), Buffers.None, Journeys.None);

        Assert.Equal(
            new Dictionary<LocalDate, Duration>
            {
                [new LocalDate(2027, 3, 27)] = Duration.FromMinutes(60),
                [new LocalDate(2027, 3, 28)] = Duration.FromMinutes(120),
            },
            DailyLoad.Of([late], London));
    }

    // §8, T3: a footprint 23:00-01:00 BST counts 60 minutes on each day.
    [Fact]
    public void AFootprintCrossingMidnight_CountsTowardsEachDay()
    {
        var late = Footprint.Around(
            new Interval(Instant.FromUtc(2026, 10, 5, 22, 0), Instant.FromUtc(2026, 10, 6, 0, 0)), Buffers.None, Journeys.None);

        var load = DailyLoad.Of([late], London);

        Assert.Equal(
            new Dictionary<LocalDate, Duration>
            {
                [new LocalDate(2026, 10, 5)] = Duration.FromMinutes(60),
                [new LocalDate(2026, 10, 6)] = Duration.FromMinutes(60),
            },
            load);
    }
}
