using FitnessTracker.Domain.Recurrence;
using FitnessTracker.Domain.Templates;
using NodaTime;

namespace FitnessTracker.Domain.Tests.Recurrence;

// CAL-9: an activity in one week of a routine's cycle (gym on Mondays and Wednesdays, week A only) happens only on the
// dates its rule falls on in that week. The gym series is Monday and Wednesday at 07:00 from Monday 5 October 2026
// (London, BST = UTC+1); week A is the week of 5 October, week B the week of 12 October.
public class SeriesOnlyOnTests
{
    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];
    private static readonly WeekCycle AB = new(weeks: 2, anchor: new LocalDate(2026, 10, 5));
    private static readonly Func<LocalDate, bool> WeekA = date => AB.WeekOf(date) == 'A';
    private static readonly Func<LocalDate, bool> WeekB = date => AB.WeekOf(date) == 'B';

    private static readonly Series Gym = new(new(2026, 10, 5), new(7, 0), Duration.FromMinutes(50), London,
        RecurrenceRule.Weekly([IsoDayOfWeek.Monday, IsoDayOfWeek.Wednesday]));

    [Fact]
    public void OnlyInWeekA_LeavesOutWeekB()
    {
        var weekA = Gym.OnlyOn(WeekA);

        LocalDate[] expected = [new(2026, 10, 5), new(2026, 10, 7), new(2026, 10, 19), new(2026, 10, 21)];
        Assert.Equal(expected, DatesOf(weekA, new(2026, 10, 5), new(2026, 10, 25)));
    }

    [Fact]
    public void FallsOn_IsOnlyTrueForTheDatesAllowed()
    {
        var weekA = Gym.OnlyOn(WeekA);

        Assert.True(weekA.FallsOn(new LocalDate(2026, 10, 5)));
        Assert.False(weekA.FallsOn(new LocalDate(2026, 10, 12)));
        Assert.False(weekA.FallsOn(new LocalDate(2026, 10, 6))); // a Tuesday: not the rule's
        Assert.True(Gym.FallsOn(new LocalDate(2026, 10, 12)));
    }

    // The change is kept with the activity, as the date its rule put it on is: it shows again if that week is allowed
    // again. A week the activity isn't in has no occurrence to show, moved or not.
    [Fact]
    public void AnOccurrenceMovedFromALeftOutDate_IsLeftOutToo()
    {
        var moved = Gym.MoveOccurrence(new LocalDate(2026, 10, 12), new LocalDateTime(2026, 10, 9, 18, 0), Duration.FromMinutes(50));

        var weekA = moved.OnlyOn(WeekA);

        Assert.Equal([new LocalDate(2026, 10, 5), new LocalDate(2026, 10, 7)], DatesOf(weekA, new(2026, 10, 5), new(2026, 10, 18)));
        Assert.Contains(new LocalDate(2026, 10, 12), weekA.Changes.Keys);
    }

    // Moved by the user into a week the activity isn't usually in: it still happens, where it was moved to.
    [Fact]
    public void AnOccurrenceMovedIntoALeftOutWeek_StillHappens()
    {
        var weekA = Gym.OnlyOn(WeekA)
            .MoveOccurrence(new LocalDate(2026, 10, 7), new LocalDateTime(2026, 10, 13, 18, 0), Duration.FromMinutes(50));

        Occurrence[] expected =
        [
            new(new(2026, 10, 5), new(Instant.FromUtc(2026, 10, 5, 6, 0), Instant.FromUtc(2026, 10, 5, 6, 50))),
            new(new(2026, 10, 7), new(Instant.FromUtc(2026, 10, 13, 17, 0), Instant.FromUtc(2026, 10, 13, 17, 50))),
        ];
        Assert.Equal(expected, weekA.OccurrencesBetween(new(2026, 10, 5), new(2026, 10, 18)));
    }

    [Fact]
    public void ChangingAnOccurrenceOnALeftOutDate_IsRefused()
    {
        var weekA = Gym.OnlyOn(WeekA);

        Assert.Throws<ArgumentException>(() => weekA.CancelOccurrence(new LocalDate(2026, 10, 12)));
        Assert.Throws<ArgumentException>(() => weekA.MoveOccurrence(new LocalDate(2026, 10, 12), new LocalDateTime(2026, 10, 12, 8, 0), Duration.FromMinutes(50)));
    }

    // OnlyOn replaces the dates allowed before; null allows every date the rule falls on again.
    [Fact]
    public void OnlyOn_ReplacesTheDatesAllowedBefore()
    {
        var weekB = Gym.OnlyOn(WeekA).OnlyOn(WeekB);
        var everyWeek = weekB.OnlyOn(null);

        Assert.Equal([new LocalDate(2026, 10, 12), new LocalDate(2026, 10, 14)], DatesOf(weekB, new(2026, 10, 5), new(2026, 10, 18)));
        Assert.Equal(4, DatesOf(everyWeek, new(2026, 10, 5), new(2026, 10, 18)).Count);
    }

    // CAL-4, P5: changing an activity from every week to week A only is an edit of the whole series. A change on a week
    // B date is on a day the edit stops it happening, so it is removed and counted, like a change on a day the rule left.
    [Fact]
    public void AnEdit_KeepsOnlyChangesOnDatesStillAllowed_AndCountsTheRest()
    {
        var changed = Gym
            .CancelOccurrence(new LocalDate(2026, 10, 7))
            .CancelOccurrence(new LocalDate(2026, 10, 12));

        var edit = changed.Edit(changed.FirstDate, changed.StartTime, changed.Length, changed.Rule, WeekA);

        Assert.Equal(1, edit.KeptChanges);
        Assert.Equal(1, edit.RemovedChanges);
        Assert.Equal([new LocalDate(2026, 10, 7)], edit.Series.Changes.Keys);
        Assert.False(edit.Series.FallsOn(new LocalDate(2026, 10, 12)));
    }

    // CAL-9, Q14: a change on a date already left out before the edit (Monday 12 October moved to 19:00, then that week
    // given to a holiday routine) wasn't taken away by the edit, so it is kept while the rule still falls on that date,
    // and shows again once the date is allowed again (Phase 6b: any edit of the activity removed it).
    [Fact]
    public void AnEdit_KeepsAChangeOnADateAlreadyLeftOut()
    {
        var moved = Gym.MoveOccurrence(new LocalDate(2026, 10, 12), new LocalDateTime(2026, 10, 12, 19, 0), Duration.FromMinutes(50))
            .OnlyOn(NotOnHoliday);

        var edit = moved.Edit(moved.FirstDate, new LocalTime(7, 30), moved.Length, moved.Rule, NotOnHoliday);

        Assert.Equal((1, 1, 0), (edit.KeptChanges, edit.HiddenChanges, edit.RemovedChanges));
        Assert.Empty(edit.Series.OccurrencesBetween(new(2026, 10, 12), new(2026, 10, 18)));
        var back = Assert.Single(edit.Series.OnlyOn(null).OccurrencesBetween(new(2026, 10, 12), new(2026, 10, 12)));
        Assert.Equal(Instant.FromUtc(2026, 10, 12, 18, 0), back.Time.Start);
    }

    // ... but not once the edited rule no longer falls on it (Mondays dropped): then nothing could show it again.
    [Fact]
    public void AnEdit_RemovesAChangeOnALeftOutDate_WhenTheRuleNoLongerFallsOnIt()
    {
        var moved = Gym.MoveOccurrence(new LocalDate(2026, 10, 12), new LocalDateTime(2026, 10, 12, 19, 0), Duration.FromMinutes(50))
            .OnlyOn(NotOnHoliday);

        var edit = moved.Edit(moved.FirstDate, moved.StartTime, moved.Length, RecurrenceRule.Weekly([IsoDayOfWeek.Wednesday]), NotOnHoliday);

        Assert.Equal((0, 1), (edit.KeptChanges, edit.RemovedChanges));
    }

    // The week of 12 October 2026 given to a holiday routine.
    private static readonly Func<LocalDate, bool> NotOnHoliday = date => date < new LocalDate(2026, 10, 12) || date > new LocalDate(2026, 10, 18);

    [Fact]
    public void AnotherTimezone_KeepsTheDatesAllowed()
    {
        var weekA = Gym.OnlyOn(WeekA).InZone(DateTimeZoneProviders.Tzdb["America/New_York"]);

        Assert.Equal([new LocalDate(2026, 10, 5), new LocalDate(2026, 10, 7)], DatesOf(weekA, new(2026, 10, 5), new(2026, 10, 18)));
    }

    private static List<LocalDate> DatesOf(Series series, LocalDate from, LocalDate to) =>
        series.OccurrencesBetween(from, to).Select(occurrence => occurrence.Date).ToList();
}
