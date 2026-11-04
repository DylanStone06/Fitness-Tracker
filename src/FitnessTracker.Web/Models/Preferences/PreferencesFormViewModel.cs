using System.ComponentModel.DataAnnotations;
using FitnessTracker.Web.Data;
using NodaTime;
using NodaTime.Text;
using NodaTime.TimeZones;

namespace FitnessTracker.Web.Models.Preferences;

/// <summary>The preferences form (ACC-3, ACC-4, ACC-7). Sleep is entered in hours, other lengths in minutes or hours.</summary>
public class PreferencesFormViewModel
{
    private static readonly LocalTimePattern ClockTime = LocalTimePattern.CreateWithInvariantCulture("HH:mm");

    /// <summary>Every place-based IANA timezone, for the list (aliases such as "GB" are left out, but still accepted).</summary>
    public static IReadOnlyList<string> TimeZoneIds { get; } = TzdbDateTimeZoneSource.Default.ZoneLocations!
        .Select(location => location.ZoneId)
        .Append("UTC")
        .Distinct()
        .Order(StringComparer.Ordinal)
        .ToList();

    [Display(Name = "Timezone")]
    [Required(ErrorMessage = "Choose your timezone.")]
    public string TimeZoneId { get; set => field = value?.Trim() ?? ""; } = UserPreferences.DefaultTimeZoneId;

    [Display(Name = "Weeks start on")]
    public IsoDayOfWeek WeekStart { get; set; } = IsoDayOfWeek.Monday;

    [Display(Name = "Clock")]
    public bool Use24HourClock { get; set; } = true;

    [Display(Name = "Distances in")]
    public DistanceUnit DistanceUnit { get; set; }

    [Display(Name = "Weights in")]
    public WeightUnit WeightUnit { get; set; }

    [Display(Name = "Sleep you aim for (hours)")]
    [Range(3, 14, ErrorMessage = "Enter between {1} and {2} hours.")]
    public decimal TargetSleepHours { get; set; } = 8;

    [Display(Name = "Least sleep that's acceptable (hours)")]
    [Range(3, 14, ErrorMessage = "Enter between {1} and {2} hours.")]
    public decimal MinimumSleepHours { get; set; } = 7;

    [Display(Name = "Usual bedtime")]
    [Required(ErrorMessage = "Enter your usual bedtime, for example 23:00.")]
    public string UsualBedtime { get; set; } = "23:00";

    [Display(Name = "Usual wake-up time")]
    [Required(ErrorMessage = "Enter your usual wake-up time, for example 07:00.")]
    public string UsualWakeTime { get; set; } = "07:00";

    [Display(Name = "Wind-down before bed (minutes)")]
    [Range(0, 180, ErrorMessage = "Enter between {1} and {2} minutes.")]
    public int WindDownMinutes { get; set; } = 30;

    [Display(Name = "Getting ready in the morning (minutes)")]
    [Range(0, 180, ErrorMessage = "Enter between {1} and {2} minutes.")]
    public int WakeUpRoutineMinutes { get; set; } = 30;

    [Display(Name = "Days you prefer to exercise")]
    public List<IsoDayOfWeek> PreferredExerciseDays { get; set; } = [];

    [Display(Name = "Time of day you prefer to exercise")]
    public PreferredTimeOfDay PreferredTimeOfDay { get; set; }

    [Display(Name = "Recovery days (no demanding sessions)")]
    public List<IsoDayOfWeek> RecoveryDays { get; set; } = [];

    [Display(Name = "Hours between demanding sessions, at least")]
    [Range(0, 168, ErrorMessage = "Enter between {1} and {2} hours.")]
    public int MinimumHoursBetweenDemandingSessions { get; set; } = 24;

    [Display(Name = "Demanding sessions in a day, at most")]
    [Range(1, 10, ErrorMessage = "Enter between {1} and {2}.")]
    public int MaximumDemandingSessionsPerDay { get; set; } = 1;

    /// <summary>The version of the saved preferences the form was opened with; empty before the first save (requirements Â§9).</summary>
    public Guid Version { get; set; }

    public static PreferencesFormViewModel From(UserPreferences preferences) => new()
    {
        TimeZoneId = preferences.TimeZoneId,
        WeekStart = preferences.WeekStart,
        Use24HourClock = preferences.Use24HourClock,
        DistanceUnit = preferences.DistanceUnit,
        WeightUnit = preferences.WeightUnit,
        TargetSleepHours = Hours(preferences.TargetSleepMinutes),
        MinimumSleepHours = Hours(preferences.MinimumSleepMinutes),
        UsualBedtime = ClockTime.Format(preferences.UsualBedtime),
        UsualWakeTime = ClockTime.Format(preferences.UsualWakeTime),
        WindDownMinutes = preferences.WindDownMinutes,
        WakeUpRoutineMinutes = preferences.WakeUpRoutineMinutes,
        PreferredExerciseDays = DaysIn(preferences.PreferredExerciseDays),
        PreferredTimeOfDay = preferences.PreferredTimeOfDay,
        RecoveryDays = DaysIn(preferences.RecoveryDays),
        MinimumHoursBetweenDemandingSessions = preferences.MinimumHoursBetweenDemandingSessions,
        MaximumDemandingSessionsPerDay = preferences.MaximumDemandingSessionsPerDay,
        Version = preferences.Version,
    };

    /// <summary>Copies the form onto the stored preferences. Only call once the form is valid.</summary>
    public void ApplyTo(UserPreferences preferences)
    {
        preferences.TimeZoneId = TimeZoneId;
        preferences.WeekStart = WeekStart;
        preferences.Use24HourClock = Use24HourClock;
        preferences.DistanceUnit = DistanceUnit;
        preferences.WeightUnit = WeightUnit;
        preferences.TargetSleepMinutes = Minutes(TargetSleepHours);
        preferences.MinimumSleepMinutes = Minutes(MinimumSleepHours);
        preferences.UsualBedtime = ClockTime.Parse(UsualBedtime).Value;
        preferences.UsualWakeTime = ClockTime.Parse(UsualWakeTime).Value;
        preferences.WindDownMinutes = WindDownMinutes;
        preferences.WakeUpRoutineMinutes = WakeUpRoutineMinutes;
        preferences.PreferredExerciseDays = SetOf(PreferredExerciseDays);
        preferences.PreferredTimeOfDay = PreferredTimeOfDay;
        preferences.RecoveryDays = SetOf(RecoveryDays);
        preferences.MinimumHoursBetweenDemandingSessions = MinimumHoursBetweenDemandingSessions;
        preferences.MaximumDemandingSessionsPerDay = MaximumDemandingSessionsPerDay;
    }

    /// <summary>
    /// The checks across the form, and those that need more than an attribute (a real timezone, a time that parses).
    /// Not IValidatableObject: ASP.NET skips that while any single field has an error, which would hide these problems
    /// until the others were fixed. The controller runs them every time instead, so every problem is listed at once.
    /// </summary>
    public IEnumerable<ValidationResult> ProblemsAcrossTheForm()
    {
        if (TimeZoneId.Length > 0 && DateTimeZoneProviders.Tzdb.GetZoneOrNull(TimeZoneId) is null)
        {
            yield return new ValidationResult("Choose a timezone from the list.", [nameof(TimeZoneId)]);
        }

        // The model binder refuses a value that isn't in an enum, even a number, but IsoDayOfWeek's "None" (0) is in
        // it, so the days are checked here.
        if (!Week.Days.Contains(WeekStart))
        {
            yield return new ValidationResult("Choose the day your weeks start on.", [nameof(WeekStart)]);
        }

        foreach (var (name, days) in new[] { (nameof(PreferredExerciseDays), PreferredExerciseDays), (nameof(RecoveryDays), RecoveryDays) })
        {
            if (days.Any(day => !Week.Days.Contains(day)))
            {
                yield return new ValidationResult("Choose days from the list.", [name]);
            }
        }

        foreach (var (name, value) in new[] { (nameof(UsualBedtime), UsualBedtime), (nameof(UsualWakeTime), UsualWakeTime) })
        {
            if (!string.IsNullOrEmpty(value) && !ClockTime.Parse(value).Success)
            {
                yield return new ValidationResult("Enter a time between 00:00 and 23:59, for example 07:00.", [name]);
            }
        }

        if (MinimumSleepHours > TargetSleepHours)
        {
            yield return new ValidationResult(
                "The least sleep that's acceptable can't be more than the sleep you aim for.", [nameof(MinimumSleepHours)]);
        }
    }

    // Two decimal places are enough to show any whole number of minutes recognisably (485 min is 8.08 h).
    private static decimal Hours(int minutes) => Math.Round(minutes / 60m, 2, MidpointRounding.AwayFromZero);

    private static int Minutes(decimal hours) => (int)Math.Round(hours * 60, MidpointRounding.AwayFromZero);

    private static List<IsoDayOfWeek> DaysIn(DaysOfWeek days) =>
        Week.Days.Where(day => days.HasFlag(FlagFor(day))).ToList();

    private static DaysOfWeek SetOf(IEnumerable<IsoDayOfWeek> days) =>
        days.Where(day => day != IsoDayOfWeek.None).Aggregate(DaysOfWeek.None, (set, day) => set | FlagFor(day));

    // Monday is 1 in both IsoDayOfWeek and ISO-8601, so Monday's flag is 1, Tuesday's 2, and so on.
    private static DaysOfWeek FlagFor(IsoDayOfWeek day) => (DaysOfWeek)(1 << ((int)day - 1));
}
