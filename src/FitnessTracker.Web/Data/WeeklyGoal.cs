namespace FitnessTracker.Web.Data;

/// <summary>
/// GOL-1: something the user wants to do a number of times each week, with no time of its own: "Gym, 3 times a week,
/// 50 minutes each, at Town gym, preferably Monday, Wednesday and Friday evenings". "Plan my week" (SCH-3) finds the
/// times; accepting one adds it as an activity, every week or that week only (requirements Q12). The session is
/// described as an activity is (category, place, buffers, demanding, priority), so a suggestion is checked like any
/// activity (SCH-1). Lengths are elapsed minutes (§8).
/// </summary>
public class WeeklyGoal : IOwnedByUser, IVersioned
{
    public const int TitleMaxLength = Activity.TitleMaxLength;

    public const int MaxSessionsPerWeek = 14;

    /// <summary>A session fits in an ordinary day (H8); the planner plans one day at a time.</summary>
    public const int MaxSessionMinutes = 24 * 60;

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string OwnerId { get; set; } = "";

    public Guid Version { get; set; }

    public string Title { get; set; } = "";

    public ActivityCategory Category { get; set; } = ActivityCategory.Other;

    /// <summary>Where each session happens (one of the user's locations), or null for nowhere in particular.</summary>
    public Guid? LocationId { get; set; }

    public int SessionsPerWeek { get; set; } = 1;

    /// <summary>Each session's length, in elapsed minutes.</summary>
    public int SessionMinutes { get; set; } = 60;

    /// <summary>Getting ready before each session (§7.1).</summary>
    public int PreparationMinutes { get; set; }

    /// <summary>Changing or showering after each session (§7.1).</summary>
    public int ChangingMinutes { get; set; }

    /// <summary>FIT-6: each session is a hard one that needs recovery afterwards (H5-H7).</summary>
    public bool IsDemanding { get; set; }

    /// <summary>The planner fits higher-priority goals in first (§7.4).</summary>
    public ActivityPriority Priority { get; set; } = ActivityPriority.Normal;

    /// <summary>§7.3: days the planner prefers (a soft preference, not a rule). None: any day.</summary>
    public DaysOfWeek PreferredDays { get; set; } = DaysOfWeek.None;

    /// <summary>§7.3: the time of day the planner prefers (a soft preference, not a rule).</summary>
    public PreferredTimeOfDay PreferredTimeOfDay { get; set; } = PreferredTimeOfDay.Any;
}
