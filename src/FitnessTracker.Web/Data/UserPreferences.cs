using NodaTime;

namespace FitnessTracker.Web.Data;

public enum DistanceUnit
{
    Kilometres,
    Miles,
}

public enum WeightUnit
{
    Kilograms,
    Pounds,
}

/// <summary>ACC-7: when someone prefers to exercise.</summary>
public enum PreferredTimeOfDay
{
    Any,
    Morning,
    Midday,
    Afternoon,
    Evening,
}

/// <summary>ACC-3: how the pages look. Dark unless chosen otherwise (the owner's choice, 20 October 2026).</summary>
public enum Appearance
{
    Dark,
    Light,
    Device,
}

/// <summary>A set of weekdays, stored as one number.</summary>
[Flags]
public enum DaysOfWeek
{
    None = 0,
    Monday = 1,
    Tuesday = 2,
    Wednesday = 4,
    Thursday = 8,
    Friday = 16,
    Saturday = 32,
    Sunday = 64,
}

/// <summary>
/// One person's preferences. Until the first save, the defaults below (requirements §3 and ACC-4) are what the
/// app uses. Lengths are whole minutes; clock times are NodaTime <see cref="LocalTime"/>s.
/// </summary>
public class UserPreferences : IOwnedByUser, IVersioned
{
    public const string DefaultTimeZoneId = "Europe/London";

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string OwnerId { get; set; } = "";

    public Guid Version { get; set; }

    // ACC-3
    public string TimeZoneId { get; set; } = DefaultTimeZoneId;

    public IsoDayOfWeek WeekStart { get; set; } = IsoDayOfWeek.Monday;

    public bool Use24HourClock { get; set; } = true;

    public DistanceUnit DistanceUnit { get; set; } = DistanceUnit.Kilometres;

    public WeightUnit WeightUnit { get; set; } = WeightUnit.Kilograms;

    public Appearance Appearance { get; set; } = Appearance.Dark;

    // ACC-4
    public int TargetSleepMinutes { get; set; } = 8 * 60;

    public int MinimumSleepMinutes { get; set; } = 7 * 60;

    public LocalTime UsualBedtime { get; set; } = new(23, 0);

    public LocalTime UsualWakeTime { get; set; } = new(7, 0);

    public int WindDownMinutes { get; set; } = 30;

    public int WakeUpRoutineMinutes { get; set; } = 30;

    // ACC-7 (used by the planner in Phase 6)
    public DaysOfWeek PreferredExerciseDays { get; set; } = DaysOfWeek.None;

    public PreferredTimeOfDay PreferredTimeOfDay { get; set; } = PreferredTimeOfDay.Any;

    public DaysOfWeek RecoveryDays { get; set; } = DaysOfWeek.None;

    public int MinimumHoursBetweenDemandingSessions { get; set; } = 24;

    public int MaximumDemandingSessionsPerDay { get; set; } = 1;
}
