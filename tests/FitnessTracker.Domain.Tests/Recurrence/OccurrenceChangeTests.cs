using FitnessTracker.Domain.Recurrence;
using NodaTime;

namespace FitnessTracker.Domain.Tests.Recurrence;

// CAL-4, T8 and principle P5: one occurrence can be cancelled or moved without touching the rest, and editing
// the whole series keeps those one-off changes and says how many there are. The gym series below is Monday,
// Wednesday and Friday at 07:00 from Monday 5 October 2026 (London, BST = UTC+1).
public class OccurrenceChangeTests
{
    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];
    private static readonly DateTimeZone NewYork = DateTimeZoneProviders.Tzdb["America/New_York"];
    private static readonly LocalDate Wednesday7th = new(2026, 10, 7);
    private static readonly LocalDate Friday9th = new(2026, 10, 9);

    private static readonly Series Gym = new(new(2026, 10, 5), new(7, 0), Duration.FromMinutes(50), London,
        RecurrenceRule.Weekly([IsoDayOfWeek.Monday, IsoDayOfWeek.Wednesday, IsoDayOfWeek.Friday]));

    // P5 (scheduling review L-3): Wednesday's session moved to 07:30, then the whole series edited to 07:30. That move
    // is now no change at all, so it isn't counted as kept (or removed): only Friday's cancellation is kept.
    [Fact]
    public void EditingTheSeriesOntoAMovedTime_DoesntCountThatMoveAsKept()
    {
        var changed = Gym
            .MoveOccurrence(Wednesday7th, new LocalDateTime(2026, 10, 7, 7, 30), Duration.FromMinutes(50))
            .CancelOccurrence(Friday9th);

        var edit = changed.Edit(changed.FirstDate, new LocalTime(7, 30), changed.Length, changed.Rule);

        Assert.Equal(1, edit.KeptChanges);
        Assert.Equal(0, edit.RemovedChanges);
        Assert.Equal([Friday9th], edit.Series.Changes.Keys);
    }

    [Fact]
    public void CancellingOneOccurrence_RemovesOnlyThatOne()
    {
        var changed = Gym.CancelOccurrence(Wednesday7th);

        LocalDate[] expected = [new(2026, 10, 5), Friday9th, new(2026, 10, 12)];
        Assert.Equal(expected, DatesOf(changed, new(2026, 10, 5), new(2026, 10, 12)));
        Assert.Contains(Wednesday7th, DatesOf(Gym, new(2026, 10, 5), new(2026, 10, 12)));
    }

    [Fact]
    public void MovingOneOccurrence_ChangesOnlyThatOne()
    {
        var changed = Gym.MoveOccurrence(Friday9th, new LocalDateTime(2026, 10, 10, 9, 0), Duration.FromHours(1));

        Occurrence[] expected =
        [
            new(new(2026, 10, 5), new(Instant.FromUtc(2026, 10, 5, 6, 0), Instant.FromUtc(2026, 10, 5, 6, 50))),
            new(Wednesday7th, new(Instant.FromUtc(2026, 10, 7, 6, 0), Instant.FromUtc(2026, 10, 7, 6, 50))),
            new(Friday9th, new(Instant.FromUtc(2026, 10, 10, 8, 0), Instant.FromUtc(2026, 10, 10, 9, 0))), // Saturday 09:00 BST
        ];
        Assert.Equal(expected, changed.OccurrencesBetween(new(2026, 10, 5), new(2026, 10, 11)));
    }

    // An occurrence belongs to the day it now starts, so it shows in the week it was moved to.
    [Fact]
    public void AMovedOccurrence_ShowsInTheWeekItWasMovedTo()
    {
        var changed = Gym.MoveOccurrence(Friday9th, new LocalDateTime(2026, 10, 12, 18, 0), Duration.FromMinutes(50));

        LocalDate[] firstWeek = [new(2026, 10, 5), Wednesday7th];
        LocalDate[] secondWeek = [new(2026, 10, 12), Friday9th, new(2026, 10, 14), new(2026, 10, 16)];
        Assert.Equal(firstWeek, DatesOf(changed, new(2026, 10, 5), new(2026, 10, 11)));
        Assert.Equal(secondWeek, DatesOf(changed, new(2026, 10, 12), new(2026, 10, 18)));
    }

    // T8: the series moves to 08:00; the cancelled Wednesday stays cancelled and the moved Friday stays moved.
    [Fact]
    public void EditingTheSeries_KeepsBothOneOffChanges_AndReportsTwo() // T8
    {
        var changed = Gym
            .CancelOccurrence(Wednesday7th)
            .MoveOccurrence(Friday9th, new LocalDateTime(2026, 10, 10, 9, 0), Duration.FromHours(1));

        var edit = changed.Edit(changed.FirstDate, new LocalTime(8, 0), changed.Length, changed.Rule);

        Assert.Equal(2, edit.KeptChanges);
        Assert.Equal(0, edit.RemovedChanges);
        Occurrence[] expected =
        [
            new(new(2026, 10, 5), new(Instant.FromUtc(2026, 10, 5, 7, 0), Instant.FromUtc(2026, 10, 5, 7, 50))), // now 08:00 BST
            new(Friday9th, new(Instant.FromUtc(2026, 10, 10, 8, 0), Instant.FromUtc(2026, 10, 10, 9, 0))), // still Saturday 09:00
            new(new(2026, 10, 12), new(Instant.FromUtc(2026, 10, 12, 7, 0), Instant.FromUtc(2026, 10, 12, 7, 50))),
        ];
        Assert.Equal(expected, edit.Series.OccurrencesBetween(new(2026, 10, 5), new(2026, 10, 12)));
    }

    // Never silent (P5): a change to a date the edited series no longer falls on is removed, and counted.
    [Fact]
    public void EditingTheSeries_RemovesChangesToDatesItNoLongerFallsOn_AndSaysHowMany()
    {
        var changed = Gym
            .CancelOccurrence(Wednesday7th)
            .MoveOccurrence(Friday9th, new LocalDateTime(2026, 10, 10, 9, 0), Duration.FromHours(1));

        var edit = changed.Edit(changed.FirstDate, changed.StartTime, changed.Length,
            RecurrenceRule.Weekly([IsoDayOfWeek.Monday, IsoDayOfWeek.Friday]));

        Assert.Equal(1, edit.KeptChanges);
        Assert.Equal(1, edit.RemovedChanges);
        LocalDate[] expected = [new(2026, 10, 5), Friday9th, new(2026, 10, 12)];
        Assert.Equal(expected, DatesOf(edit.Series, new(2026, 10, 5), new(2026, 10, 12)));
    }

    [Fact]
    public void RestoringAnOccurrence_UndoesItsOneOffChange()
    {
        var changed = Gym.CancelOccurrence(Wednesday7th);

        var restored = changed.RestoreOccurrence(Wednesday7th);

        Assert.Empty(restored.Changes);
        Assert.Contains(Wednesday7th, DatesOf(restored, new(2026, 10, 5), new(2026, 10, 11)));
    }

    // Dragging an occurrence away and back again leaves no one-off change to report.
    [Fact]
    public void MovingAnOccurrenceBackToItsUsualTime_LeavesNoOneOffChange()
    {
        var moved = Gym.MoveOccurrence(Friday9th, new LocalDateTime(2026, 10, 10, 9, 0), Duration.FromHours(1));

        var movedBack = moved.MoveOccurrence(Friday9th, Friday9th.At(Gym.StartTime), Gym.Length);

        Assert.Empty(movedBack.Changes);
    }

    // Found by the first scheduling review: on 28 March 2027 a 01:30 series really occurs at 02:30 BST, so a move to
    // 02:30 is a move to where it already is. "Back to its usual time" compares moments, not clock times.
    [Fact]
    public void MovingAnOccurrenceToTheMomentItAlreadyHappens_OnTheNightTheClocksGoForward_LeavesNoChange() // T5
    {
        var series = new Series(new(2027, 3, 22), new(1, 30), Duration.FromMinutes(50), London, RecurrenceRule.Daily());

        var moved = series.MoveOccurrence(new(2027, 3, 28), new LocalDateTime(2027, 3, 28, 2, 30), Duration.FromMinutes(50));

        Assert.Empty(moved.Changes);
    }

    // An occurrence moved earlier, from after the dates asked for into them, still shows.
    [Fact]
    public void AnOccurrenceMovedEarlier_IntoTheDatesAskedFor_Shows()
    {
        var friday16th = new LocalDate(2026, 10, 16);
        var changed = Gym.MoveOccurrence(friday16th, new LocalDateTime(2026, 10, 10, 9, 0), Duration.FromMinutes(50));

        LocalDate[] expected = [new(2026, 10, 5), Wednesday7th, Friday9th, friday16th];
        Assert.Equal(expected, DatesOf(changed, new(2026, 10, 5), new(2026, 10, 11)));
    }

    [Fact]
    public void ChangingAnOccurrenceTheSeriesDoesNotHave_IsRefused()
    {
        var tuesday6th = new LocalDate(2026, 10, 6);

        Assert.Throws<ArgumentException>(() => Gym.CancelOccurrence(tuesday6th));
        Assert.Throws<ArgumentException>(() => Gym.MoveOccurrence(tuesday6th, new LocalDateTime(2026, 10, 6, 9, 0), Duration.FromHours(1)));
    }

    // As in iCalendar, a cancelled occurrence still counts towards "ends after N": cancelling one of three sessions
    // leaves two, rather than adding a fourth.
    [Fact]
    public void ACancelledOccurrence_StillCountsTowardsTheSeriesCount()
    {
        var threeSessions = new Series(Gym.FirstDate, Gym.StartTime, Gym.Length, London, Gym.Rule.EndingAfter(3));

        var changed = threeSessions.CancelOccurrence(Wednesday7th);

        LocalDate[] expected = [new(2026, 10, 5), Friday9th];
        Assert.Equal(expected, DatesOf(changed, new(2026, 10, 1), new(2026, 10, 31)));
    }

    // T6 with one-off changes: they keep their clock times in the new timezone too.
    [Fact]
    public void InAnotherZone_OneOffChangesKeepTheirClockTimes()
    {
        var changed = Gym
            .CancelOccurrence(Wednesday7th)
            .MoveOccurrence(Friday9th, new LocalDateTime(2026, 10, 10, 9, 0), Duration.FromHours(1));

        var inNewYork = changed.InZone(NewYork);

        Occurrence[] expected =
        [
            new(new(2026, 10, 5), new(Instant.FromUtc(2026, 10, 5, 11, 0), Instant.FromUtc(2026, 10, 5, 11, 50))), // 07:00 EDT
            new(Friday9th, new(Instant.FromUtc(2026, 10, 10, 13, 0), Instant.FromUtc(2026, 10, 10, 14, 0))), // 09:00 EDT
        ];
        Assert.Equal(expected, inNewYork.OccurrencesBetween(new(2026, 10, 5), new(2026, 10, 11)));
    }

    private static IEnumerable<LocalDate> DatesOf(Series series, LocalDate from, LocalDate to) =>
        series.OccurrencesBetween(from, to).Select(occurrence => occurrence.Date);
}
