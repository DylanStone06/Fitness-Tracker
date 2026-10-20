using FitnessTracker.Domain.Footprints;
using FitnessTracker.Domain.Recurrence;
using FitnessTracker.Domain.Scheduling;
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
    /// The domain's view of this activity, for the check on save (SCH-1). A one-off is a series that happens once, so
    /// both turn clock times into moments the same way (§8: 01:30 on the night the clocks go forward is 02:30).
    /// </summary>
    public PlannedActivity ToPlannedActivity() =>
        new(Id,
            Title,
            new Series(
                StartDate,
                StartTime,
                Duration.FromMinutes(LengthMinutes),
                DateTimeZoneProviders.Tzdb[TimeZoneId],
                RepeatRule is null ? RecurrenceRule.Daily().EndingAfter(1) : RecurrenceRuleText.Parse(RepeatRule)),
            new Buffers(Duration.FromMinutes(PreparationMinutes), Duration.FromMinutes(ChangingMinutes)),
            LocationId);
}
