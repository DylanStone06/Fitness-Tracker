using FitnessTracker.Domain.Footprints;
using FitnessTracker.Domain.Recurrence;
using FitnessTracker.Domain.Scheduling;
using FitnessTracker.Domain.Templates;
using NodaTime;

namespace FitnessTracker.Web.Data;

/// <summary>CAL-2: what kind of activity it is. The calendar (Phase 5) gives each its own colour.</summary>
public enum ActivityCategory
{
    Work,
    Commute,
    Gym,
    Run,
    OtherExercise,
    MealPrep,
    Social,
    Appointment,
    Other,
}

/// <summary>What the categories mean beyond their names.</summary>
public static class ActivityCategories
{
    /// <summary>ACC-7: the categories Preferences' exercise days and time of day apply to.</summary>
    public static bool IsExercise(this ActivityCategory category) =>
        category is ActivityCategory.Gym or ActivityCategory.Run or ActivityCategory.OtherExercise;
}

/// <summary>CAL-2: the planner fits higher-priority activities in first (§7.4).</summary>
public enum ActivityPriority
{
    Low,
    Normal,
    High,
}

/// <summary>
/// CAL-2, CAL-3: something that takes up time, once or repeating. Work is an activity too (category Work, at the
/// office location, repeating on workdays), so ACC-5's work pattern isn't a separate record; a day off will be a
/// cancelled occurrence (Phase 5). Stored as clock time plus an IANA timezone (§8, CLAUDE.md rule 2): the user's
/// timezone when it was added, and moved to their new one, keeping its clock time, when they change it (§8, T6:
/// "gym at 7am wherever I am"). The length is elapsed time (§8).
/// </summary>
public class Activity : IOwnedByUser, IVersioned
{
    public const int TitleMaxLength = 100;

    public const int NotesMaxLength = 2000;

    public const int MaxBufferMinutes = 240;

    public const int RepeatRuleMaxLength = 200;

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string OwnerId { get; set; } = "";

    public Guid Version { get; set; }

    public string Title { get; set; } = "";

    public ActivityCategory Category { get; set; } = ActivityCategory.Other;

    /// <summary>Where it happens (one of the user's locations), or null for nowhere in particular: no travel is added.</summary>
    public Guid? LocationId { get; set; }

    /// <summary>The planner never moves a fixed activity (H2); it may suggest another time for a movable one.</summary>
    public bool IsFixed { get; set; } = true;

    public ActivityPriority Priority { get; set; } = ActivityPriority.Normal;

    /// <summary>FIT-6: a hard session that needs recovery afterwards (H5-H7).</summary>
    public bool IsDemanding { get; set; }

    /// <summary>Getting ready beforehand, part of the footprint (§7.1).</summary>
    public int PreparationMinutes { get; set; }

    /// <summary>Changing or showering afterwards, part of the footprint (§7.1).</summary>
    public int ChangingMinutes { get; set; }

    public string? Notes { get; set; }

    /// <summary>
    /// The date of a one-off activity, or the first date of a repeating one, which only counts if the rule falls on
    /// it (see <see cref="FitnessTracker.Domain.Recurrence.RecurrenceRule"/>).
    /// </summary>
    public LocalDate StartDate { get; set; }

    public LocalTime StartTime { get; set; }

    /// <summary>The IANA timezone its clock times are in, such as "Europe/London".</summary>
    public string TimeZoneId { get; set; } = "";

    /// <summary>Elapsed minutes. One that runs past midnight ends the next day.</summary>
    public int LengthMinutes { get; set; }

    /// <summary>CAL-3: how it repeats, as RRULE text (ADR 0001, RecurrenceRuleText), or null for a one-off.</summary>
    public string? RepeatRule { get; set; }

    /// <summary>
    /// CAL-4: occurrences of a repeating activity cancelled or moved on their own (OccurrenceChangesText), or null
    /// for none. Always null for a one-off, which is changed directly.
    /// </summary>
    public string? OccurrenceChanges { get; set; }

    /// <summary>GOL-1: the weekly goal it was added for, whose sessions it counts towards when the week is planned.</summary>
    public Guid? GoalId { get; set; }

    /// <summary>
    /// CAL-9: the routine it belongs to, so it only happens while that routine is in use; null for every week, whatever
    /// the routine. Always null for a one-off.
    /// </summary>
    public Guid? RoutineId { get; set; }

    /// <summary>CAL-9: the one week of its routine's cycle it happens in ('A' to 'D'), or null for every week of it.</summary>
    public char? RoutineWeek { get; set; }

    /// <summary>
    /// CAL-9: a copy of this activity in <paramref name="routine"/> (a duplicated routine), in the same week of it, as a
    /// new activity. Changes made to single occurrences aren't copied: they are on dates in this one's weeks.
    /// </summary>
    public Activity CopyInto(Guid routine) => new()
    {
        Title = Title,
        Category = Category,
        LocationId = LocationId,
        IsFixed = IsFixed,
        Priority = Priority,
        IsDemanding = IsDemanding,
        PreparationMinutes = PreparationMinutes,
        ChangingMinutes = ChangingMinutes,
        Notes = Notes,
        StartDate = StartDate,
        StartTime = StartTime,
        TimeZoneId = TimeZoneId,
        LengthMinutes = LengthMinutes,
        RepeatRule = RepeatRule,
        GoalId = GoalId,
        RoutineId = routine,
        RoutineWeek = RoutineWeek,
    };

    /// <summary>
    /// The domain's view of this activity, for the check on save (SCH-1) and the home page. A one-off is a series that
    /// happens once, so both turn clock times into moments the same way (§8: 01:30 on the night the clocks go forward
    /// is 02:30). One in a routine happens only on the dates <paramref name="routines"/> says it is in use, in its week
    /// (CAL-9). Throws FormatException (or DateTimeZoneNotFoundException) for a row the app couldn't have written,
    /// such as one edited by hand, so callers can leave it out.
    /// </summary>
    public PlannedActivity ToPlannedActivity(RoutineCalendar routines) => LengthMinutes <= 0
        ? throw new FormatException($"{Title} has a length of {LengthMinutes} minutes.")
        : new(Id,
            Title,
            OccurrenceChangesText.Apply(UsualSeries(), OccurrenceChanges).OnlyOn(HappensOn(routines)),
            new Buffers(Duration.FromMinutes(PreparationMinutes), Duration.FromMinutes(ChangingMinutes)),
            LocationId)
        {
            GoalId = GoalId,
            IsDemanding = IsDemanding,
        };

    /// <summary>
    /// CAL-4, T8: after the whole activity is edited, keeps the one-off changes of <paramref name="before"/> (the
    /// activity as it was, in the weeks it was in) on dates it still falls on, in the weeks it is in now (CAL-9), and
    /// removes the rest (all of them for a one-off). A change on a date it already didn't happen on (a week since given
    /// to a holiday routine, Q14) wasn't taken away by the edit, so it is kept while the repeat still falls on that
    /// date. Returns how many of each, to tell the user.
    /// </summary>
    public (int Kept, int Removed) KeepChangesFrom(Series before, RoutineCalendar routines)
    {
        if (RepeatRule is null)
        {
            OccurrenceChanges = null;
            return (0, before.Changes.Count);
        }

        var edit = before.Edit(StartDate, StartTime, Duration.FromMinutes(LengthMinutes), RecurrenceRuleText.Parse(RepeatRule), HappensOn(routines));
        OccurrenceChanges = OccurrenceChangesText.Format(edit.Series.Changes);
        return (edit.KeptChanges, edit.RemovedChanges);
    }

    // CAL-9: the dates it may happen on, or null for every date its rule falls on. The routine and week are copied now,
    // not read when a date is asked about: a series made before an edit must keep the weeks it was made with
    // (scheduling review, Phase 6b: the week was read after the form had changed it).
    private Func<LocalDate, bool>? HappensOn(RoutineCalendar routines)
    {
        if (RoutineId is not { } routine)
        {
            return null;
        }

        var week = RoutineWeek;
        return date => routines.Includes(routine, week, date);
    }

    // The series as its rule makes it, without one-off changes.
    private Series UsualSeries() => new(
        StartDate,
        StartTime,
        Duration.FromMinutes(LengthMinutes),
        DateTimeZoneProviders.Tzdb[TimeZoneId],
        RepeatRule is null ? RecurrenceRule.Daily().EndingAfter(1) : RecurrenceRuleText.Parse(RepeatRule));
}
