using FitnessTracker.Domain.Footprints;
using FitnessTracker.Domain.Scheduling;
using NodaTime;

namespace FitnessTracker.Domain.Tests.Scheduling;

// H1: footprints don't overlap; intervals are half-open. Conflicts are checked on footprints, not on the bare
// activities (glossary), and each conflict names the parts that clash so it can be explained (CAL-6, SCH-5).
// All times are Monday 5 October 2026, BST (UTC+1); expected moments are written in UTC by hand.
public class ConflictsTests
{
    private static Instant Bst(int hour, int minute) => Instant.FromUtc(2026, 10, 5, hour - 1, minute);

    private static Booking AtBase(string label, Instant start, Instant end, Buffers? buffers = null) =>
        new(label, Footprint.Around(new Interval(start, end), buffers ?? Buffers.None, Journeys.None));

    // T11: back-to-back at 10:00 is not a conflict.
    [Fact]
    public void BackToBackActivities_DoNotConflict()
    {
        var meeting = AtBase("Meeting", Bst(9, 0), Bst(10, 0));
        var call = AtBase("Call", Bst(10, 0), Bst(11, 0));

        Assert.Empty(Conflicts.Between(call, [meeting]));
    }

    // T11: overlapping by one minute is a conflict, and says by how much.
    [Fact]
    public void OverlappingByOneMinute_IsAConflict()
    {
        var meeting = AtBase("Meeting", Bst(9, 0), Bst(10, 0));
        var call = AtBase("Call", Bst(9, 59), Bst(11, 0));

        var clash = Assert.Single(Conflicts.Between(call, [meeting]));

        Assert.Equal(new Interval(Instant.FromUtc(2026, 10, 5, 8, 59), Instant.FromUtc(2026, 10, 5, 9, 0)), clash.Overlap);
        Assert.Equal((FootprintPart.Activity, FootprintPart.Activity), (clash.CandidatePart, clash.OtherPart));
        Assert.Same(meeting, clash.Other);
    }

    // T10: the gym at 18:00-18:50 and dinner at 18:55 don't touch, but 10 minutes' changing after the gym runs into
    // dinner. Reported as changing overlapping the dinner itself, 18:55-19:00 BST.
    [Fact]
    public void ChangingTimeRunningIntoAnotherActivity_IsAConflict_NamingTheChangingTime()
    {
        var gym = AtBase("Gym", Bst(18, 0), Bst(18, 50), new Buffers(Preparation: Duration.Zero, Changing: Duration.FromMinutes(10)));
        var dinner = AtBase("Dinner", Bst(18, 55), Bst(20, 0));

        var clash = Assert.Single(Conflicts.Between(gym, [dinner]));

        Assert.Equal((FootprintPart.Changing, FootprintPart.Activity), (clash.CandidatePart, clash.OtherPart));
        Assert.Equal(new Interval(Instant.FromUtc(2026, 10, 5, 17, 55), Instant.FromUtc(2026, 10, 5, 18, 0)), clash.Overlap);
    }

    // T10: 15 minutes' preparation before the gym (17:45-18:00) overlaps a call ending at 17:50.
    [Fact]
    public void PreparationTimeOverlappingAnotherActivity_IsAConflict_NamingThePreparation()
    {
        var gym = AtBase("Gym", Bst(18, 0), Bst(18, 50), new Buffers(Preparation: Duration.FromMinutes(15), Changing: Duration.Zero));
        var call = AtBase("Call", Bst(17, 30), Bst(17, 50));

        var clash = Assert.Single(Conflicts.Between(gym, [call]));

        Assert.Equal((FootprintPart.Preparation, FootprintPart.Activity), (clash.CandidatePart, clash.OtherPart));
        Assert.Equal(new Interval(Instant.FromUtc(2026, 10, 5, 16, 45), Instant.FromUtc(2026, 10, 5, 16, 50)), clash.Overlap);
    }

    // When several parts overlap, the clash names the pair where the overlap begins: here the gym's travel there
    // (17:35-18:00) runs into a meeting (17:00-18:10), and the workout itself (from 18:00) overlaps it too, so a
    // shorter journey alone wouldn't fix it: the clash says so.
    [Fact]
    public void AClash_NamesThePartsWhereTheOverlapBegins_AndWhetherTheActivitiesThemselvesOverlap()
    {
        var gym = new Booking("Gym", Footprint.Around(
            new Interval(Bst(18, 0), Bst(18, 50)), Buffers.None, new Journeys(Out: Duration.FromMinutes(25), Back: Duration.FromMinutes(45))));
        var meeting = AtBase("Meeting", Bst(17, 0), Bst(18, 10));

        var clash = Assert.Single(Conflicts.Between(gym, [meeting]));

        Assert.Equal((FootprintPart.TravelOut, FootprintPart.Activity), (clash.CandidatePart, clash.OtherPart));
        Assert.Equal(new Interval(Instant.FromUtc(2026, 10, 5, 16, 35), Instant.FromUtc(2026, 10, 5, 17, 10)), clash.Overlap);
        Assert.True(clash.ActivitiesOverlap);
    }

    // T10's point: the activities themselves don't touch, only the changing time after the gym does.
    [Fact]
    public void AClashCausedOnlyByABuffer_SaysTheActivitiesThemselvesDoNotOverlap()
    {
        var gym = AtBase("Gym", Bst(18, 0), Bst(18, 50), new Buffers(Preparation: Duration.Zero, Changing: Duration.FromMinutes(10)));
        var dinner = AtBase("Dinner", Bst(18, 55), Bst(20, 0));

        Assert.False(Assert.Single(Conflicts.Between(gym, [dinner])).ActivitiesOverlap);
    }

    // Several clashes come back in the order their overlaps begin.
    [Fact]
    public void Between_ListsSeveralClashes_InTheOrderTheyBegin()
    {
        var work = AtBase("Work", Bst(9, 0), Bst(17, 0));
        var lunch = AtBase("Lunch", Bst(12, 0), Bst(13, 0));
        var call = AtBase("Call", Bst(10, 0), Bst(10, 30));

        Assert.Equal(["Call", "Lunch"], Conflicts.Between(work, [lunch, call]).Select(clash => clash.Other.Label));
    }

    // SCH-7: the same bookings give the same answer whatever order they arrive in, even when times tie. Clashes that
    // begin together are ordered by when the other booking starts (the call, 17:30, before the meeting, 17:45);
    // bookings that start together by when they end, then by label.
    [Fact]
    public void ClashesThatBeginTogether_ComeOutTheSameWhateverTheInputOrder()
    {
        var gym = AtBase("Gym", Bst(18, 0), Bst(19, 0));
        var call = AtBase("Call", Bst(17, 30), Bst(18, 30));
        var meeting = AtBase("Meeting", Bst(17, 45), Bst(18, 15));
        var run = AtBase("Run", Bst(9, 0), Bst(10, 0));
        var shortCall = AtBase("Short call", Bst(9, 0), Bst(9, 30));

        Assert.Equal(["Call", "Meeting"], Conflicts.Between(gym, [meeting, call]).Select(clash => clash.Other.Label));
        Assert.Equal(["Call", "Meeting"], Conflicts.Between(gym, [call, meeting]).Select(clash => clash.Other.Label));
        var oneWay = Assert.Single(Conflicts.Among([run, shortCall]));
        var otherWay = Assert.Single(Conflicts.Among([shortCall, run]));
        Assert.Equal(("Short call", "Run"), (oneWay.Candidate.Label, oneWay.Other.Label));
        Assert.Equal(("Short call", "Run"), (otherWay.Candidate.Label, otherWay.Other.Label));
    }

    // CAL-6: on the calendar every pair of clashing bookings is reported, each pair once, in time order.
    [Fact]
    public void Among_ReportsEachClashingPairOnce_InTimeOrder()
    {
        var a = AtBase("A", Bst(9, 0), Bst(10, 0));
        var b = AtBase("B", Bst(9, 30), Bst(10, 30));
        var c = AtBase("C", Bst(10, 15), Bst(11, 0));
        var d = AtBase("D", Bst(12, 0), Bst(13, 0));

        var clashes = Conflicts.Among([d, c, b, a]);

        Assert.Equal([("A", "B"), ("B", "C")], clashes.Select(clash => (clash.Candidate.Label, clash.Other.Label)));
    }

    // CAL-6: a long booking clashes with everything inside it, not only its neighbour in time.
    [Fact]
    public void Among_FindsEveryClashWithALongBooking()
    {
        var work = AtBase("Work", Bst(9, 0), Bst(17, 0));
        var call = AtBase("Call", Bst(10, 0), Bst(10, 30));
        var lunch = AtBase("Lunch", Bst(12, 0), Bst(13, 0));

        var clashes = Conflicts.Among([lunch, call, work]);

        Assert.Equal([("Work", "Call"), ("Work", "Lunch")], clashes.Select(clash => (clash.Candidate.Label, clash.Other.Label)));
        Assert.Equal(new Interval(Instant.FromUtc(2026, 10, 5, 11, 0), Instant.FromUtc(2026, 10, 5, 12, 0)), clashes[1].Overlap);
    }

    // §8: conflicts compare moments, not clock times. Sunday 25 October 2026: a film from 00:30 BST (23:30 UTC on the
    // 24th) to the second 01:00 (01:00 UTC), and a call at the first 01:30 (00:30-00:50 UTC). On the clock they look
    // apart (00:30-01:00 and 01:30-01:50), but the call happens during the film.
    [Fact]
    public void OnTheNightTheClocksGoBack_ConflictsCompareMoments_NotClockTimes()
    {
        var film = new Booking("Film", Footprint.Around(
            new Interval(Instant.FromUtc(2026, 10, 24, 23, 30), Instant.FromUtc(2026, 10, 25, 1, 0)), Buffers.None, Journeys.None));
        var call = new Booking("Call", Footprint.Around(
            new Interval(Instant.FromUtc(2026, 10, 25, 0, 30), Instant.FromUtc(2026, 10, 25, 0, 50)), Buffers.None, Journeys.None));

        var clash = Assert.Single(Conflicts.Between(call, [film]));

        Assert.Equal(new Interval(Instant.FromUtc(2026, 10, 25, 0, 30), Instant.FromUtc(2026, 10, 25, 0, 50)), clash.Overlap);
    }
}
