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

    // Q17: moved by the user from a date later left out (Monday 12 October's to Friday 9 October, then week B given to
    // something else) to a date it is in use on: it still happens where it was moved to, known by the date its rule put
    // it on (until 4 November 2026 it was left out, without a word).
    [Fact]
    public void Q17_AnOccurrenceMovedFromALeftOutDate_ToADateInUse_StillHappens()
    {
        var moved = Gym.MoveOccurrence(new LocalDate(2026, 10, 12), new LocalDateTime(2026, 10, 9, 18, 0), Duration.FromMinutes(50));

        var weekA = moved.OnlyOn(WeekA);

        Occurrence[] expected =
        [
            new(new(2026, 10, 5), new(Instant.FromUtc(2026, 10, 5, 6, 0), Instant.FromUtc(2026, 10, 5, 6, 50))),
            new(new(2026, 10, 7), new(Instant.FromUtc(2026, 10, 7, 6, 0), Instant.FromUtc(2026, 10, 7, 6, 50))),
            new(new(2026, 10, 12), new(Instant.FromUtc(2026, 10, 9, 17, 0), Instant.FromUtc(2026, 10, 9, 17, 50))),
        ];
        Assert.Equal(expected, weekA.OccurrencesBetween(new(2026, 10, 5), new(2026, 10, 18)));
        Assert.True(weekA.FallsOn(new LocalDate(2026, 10, 12)));
    }

    // ... but one moved from a date left out to another date left out (Monday 12 October's to Wednesday 14 October at
    // 18:00, both week B) goes with that week, as an unmoved one does. The change is kept, and shows again if the week
    // is allowed again.
    [Fact]
    public void AnOccurrenceMovedWithinALeftOutWeek_IsLeftOut()
    {
        var moved = Gym.MoveOccurrence(new LocalDate(2026, 10, 12), new LocalDateTime(2026, 10, 14, 18, 0), Duration.FromMinutes(50));

        var weekA = moved.OnlyOn(WeekA);

        Assert.Empty(weekA.OccurrencesBetween(new(2026, 10, 12), new(2026, 10, 18)));
        Assert.False(weekA.FallsOn(new LocalDate(2026, 10, 12)));
        Assert.Contains(new LocalDate(2026, 10, 12), weekA.Changes.Keys);
    }

    // Q17: a kept move is a session like any other: it can be moved again or cancelled. Putting it back leaves it out,
    // since its own date isn't in use (the occurrence page says so first).
    [Fact]
    public void AKeptMove_CanBeChangedAgain()
    {
        var monday = new LocalDate(2026, 10, 12);
        var weekA = Gym.MoveOccurrence(monday, new LocalDateTime(2026, 10, 9, 18, 0), Duration.FromMinutes(50)).OnlyOn(WeekA);

        var movedAgain = weekA.MoveOccurrence(monday, new LocalDateTime(2026, 10, 10, 9, 0), Duration.FromMinutes(50));
        var cancelled = weekA.CancelOccurrence(monday);
        var putBack = weekA.RestoreOccurrence(monday);

        Assert.Equal(Instant.FromUtc(2026, 10, 10, 8, 0), movedAgain.OccurrencesBetween(new(2026, 10, 5), new(2026, 10, 18)).Single(occurrence => occurrence.Date == monday).Time.Start);
        Assert.DoesNotContain(monday, DatesOf(cancelled, new(2026, 10, 5), new(2026, 10, 18)));
        Assert.DoesNotContain(monday, DatesOf(putBack, new(2026, 10, 5), new(2026, 10, 18)));
    }

    // §8, T5 with Q17: the same on the night the clocks go forward. A daily 01:30 series has Sunday 28 March 2027's
    // occurrence (really 02:30 BST) moved to Friday 26 March at 08:00 (GMT, so 08:00 UTC); that weekend is then left
    // out. It still happens on the Friday, beside Friday's own; the weekend's own don't.
    [Fact]
    public void Q17_AMoveOutOfALeftOutWeekend_StillHappens_OnTheNightTheClocksGoForward()
    {
        var nightly = new Series(new(2027, 3, 22), new(1, 30), Duration.FromMinutes(30), London, RecurrenceRule.Daily());
        Func<LocalDate, bool> notThatWeekend = date => date < new LocalDate(2027, 3, 27) || date > new LocalDate(2027, 3, 28);
        var moved = nightly.MoveOccurrence(new LocalDate(2027, 3, 28), new LocalDateTime(2027, 3, 26, 8, 0), Duration.FromMinutes(30))
            .OnlyOn(notThatWeekend);

        Occurrence[] expected =
        [
            new(new(2027, 3, 26), new(Instant.FromUtc(2027, 3, 26, 1, 30), Instant.FromUtc(2027, 3, 26, 2, 0))),
            new(new(2027, 3, 28), new(Instant.FromUtc(2027, 3, 26, 8, 0), Instant.FromUtc(2027, 3, 26, 8, 30))),
        ];
        Assert.Equal(expected, moved.OccurrencesBetween(new(2027, 3, 26), new(2027, 3, 28)));
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

    // §8, T5: the same on the night the clocks go forward. A daily 01:30 series has its Sunday 28 March 2027 occurrence
    // (really 02:30, BST) moved to 08:00; that weekend is then given to a holiday; editing the series to 45 minutes keeps
    // the hidden move, which is back at 08:00 BST (07:00 UTC) when the date is allowed again (scheduling review, Phase 6b:
    // no test on a clock-change night).
    [Fact]
    public void AnEdit_KeepsAChangeOnADateAlreadyLeftOut_OnTheNightTheClocksGoForward()
    {
        var nightly = new Series(new(2027, 3, 22), new(1, 30), Duration.FromMinutes(30), London, RecurrenceRule.Daily());
        Func<LocalDate, bool> notThatWeekend = date => date < new LocalDate(2027, 3, 27) || date > new LocalDate(2027, 3, 28);
        var moved = nightly.MoveOccurrence(new LocalDate(2027, 3, 28), new LocalDateTime(2027, 3, 28, 8, 0), Duration.FromMinutes(30))
            .OnlyOn(notThatWeekend);

        var edit = moved.Edit(moved.FirstDate, moved.StartTime, Duration.FromMinutes(45), moved.Rule, notThatWeekend);

        Assert.Equal((1, 1, 0), (edit.KeptChanges, edit.HiddenChanges, edit.RemovedChanges));
        var back = Assert.Single(edit.Series.OnlyOn(null).OccurrencesBetween(new(2027, 3, 28), new(2027, 3, 28)));
        Assert.Equal(Instant.FromUtc(2027, 3, 28, 7, 0), back.Time.Start);
    }

    // Q17: a move out of that week (Monday 12 October's to Friday 9 October, then 12-18 October given to a holiday) is
    // kept by an edit too, and isn't hidden: it is on a date in use, so it shows.
    [Fact]
    public void AnEdit_KeepsAMoveOutOfALeftOutWeek_AndItIsntHidden()
    {
        var moved = Gym.MoveOccurrence(new LocalDate(2026, 10, 12), new LocalDateTime(2026, 10, 9, 18, 0), Duration.FromMinutes(50))
            .OnlyOn(NotOnHoliday);

        var edit = moved.Edit(moved.FirstDate, new LocalTime(7, 30), moved.Length, moved.Rule, NotOnHoliday);

        Assert.Equal((1, 0, 0), (edit.KeptChanges, edit.HiddenChanges, edit.RemovedChanges));
        var friday = Assert.Single(edit.Series.OccurrencesBetween(new(2026, 10, 9), new(2026, 10, 9)));
        Assert.Equal(Instant.FromUtc(2026, 10, 9, 17, 0), friday.Time.Start);
    }

    // Q17 with P5 (scheduling review of Q17, M2): changing an every-week activity to week A only, where Monday 12
    // October's (week B) was moved to Friday 9 October (week A), keeps that session where it was moved to, since it still
    // happens there, and gives the same answer whether or not a holiday covered 12 October when the edit was made
    // (without one it was removed; with one, kept).
    [Fact]
    public void AnEditToWeekAOnly_KeepsAWeekBSessionMovedIntoWeekA_WithOrWithoutAHoliday()
    {
        var moved = Gym.MoveOccurrence(new LocalDate(2026, 10, 12), new LocalDateTime(2026, 10, 9, 18, 0), Duration.FromMinutes(50));
        Func<LocalDate, bool> weekAOffHoliday = date => WeekA(date) && NotOnHoliday(date);

        var plain = moved.Edit(moved.FirstDate, moved.StartTime, moved.Length, moved.Rule, WeekA);
        var duringHoliday = moved.OnlyOn(NotOnHoliday).Edit(moved.FirstDate, moved.StartTime, moved.Length, moved.Rule, weekAOffHoliday);

        Assert.Equal((1, 0, 0), (plain.KeptChanges, plain.HiddenChanges, plain.RemovedChanges));
        Assert.Equal((1, 0, 0), (duringHoliday.KeptChanges, duringHoliday.HiddenChanges, duringHoliday.RemovedChanges));
        Assert.Contains(plain.Series.OccurrencesBetween(new(2026, 10, 5), new(2026, 10, 11)), occurrence => occurrence.Time.Start == Instant.FromUtc(2026, 10, 9, 17, 0));
    }

    // P5: an edit that stops a kept move being seen (it happens on Friday 9 October, in week A, though 12 October is in a
    // holiday; the edit makes the activity week B only) removes it and says so, rather than hiding it for later
    // (scheduling review of Q17, L5).
    [Fact]
    public void AnEdit_ThatStopsAKeptMoveBeingSeen_RemovesIt()
    {
        var moved = Gym.MoveOccurrence(new LocalDate(2026, 10, 12), new LocalDateTime(2026, 10, 9, 18, 0), Duration.FromMinutes(50))
            .OnlyOn(NotOnHoliday);
        Func<LocalDate, bool> weekBOffHoliday = date => WeekB(date) && NotOnHoliday(date);

        var edit = moved.Edit(moved.FirstDate, moved.StartTime, moved.Length, moved.Rule, weekBOffHoliday);

        Assert.Equal((0, 0, 1), (edit.KeptChanges, edit.HiddenChanges, edit.RemovedChanges));
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
