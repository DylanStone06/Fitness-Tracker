using System.Collections.Frozen;
using FitnessTracker.Domain.Recurrence;
using FitnessTracker.Domain.Time;
using FitnessTracker.Web.Data;
using NodaTime;
using NodaTime.Text;

namespace FitnessTracker.Web.Models.Activities;

/// <summary>How an activity is described on the pages: its category's name, and when it happens in plain English.</summary>
public static class ActivityDisplay
{
    private static readonly LocalDatePattern Date = DateText.Named;

    public static string CategoryName(ActivityCategory category) => category switch
    {
        ActivityCategory.OtherExercise => "Other exercise",
        ActivityCategory.MealPrep => "Meal prep",
        _ => category.ToString(),
    };

    /// <summary>CAL-2: each category's colour, as a class in site.css (docs/design.md).</summary>
    public static readonly FrozenDictionary<ActivityCategory, string> CategoryStyles = new Dictionary<ActivityCategory, string>
    {
        [ActivityCategory.Work] = "cat-work",
        [ActivityCategory.Commute] = "cat-work",
        [ActivityCategory.Gym] = "cat-exercise",
        [ActivityCategory.Run] = "cat-exercise",
        [ActivityCategory.OtherExercise] = "cat-exercise",
        [ActivityCategory.MealPrep] = "cat-meal",
        [ActivityCategory.Social] = "cat-social",
        [ActivityCategory.Appointment] = "cat-social",
        [ActivityCategory.Other] = "cat-other",
    }.ToFrozenDictionary();

    /// <summary>
    /// The class giving a category its colour. It goes with the category's name, never instead of it (WCAG 1.4.1).
    /// A value no category has is grey.
    /// </summary>
    public static string CategoryStyle(ActivityCategory category) => CategoryStyles.GetValueOrDefault(category, "cat-other");

    /// <summary>
    /// For example "Fri 20 Nov 2026 at 09:15 for 30 min", or "Every 2 weeks on Mon, Wed at 07:00 for 1 h, from
    /// Mon 2 Nov 2026, 10 times", with times as the user chose (ACC-3: "7:00am").
    /// </summary>
    public static string When(Activity activity, ClockFormat clock = ClockFormat.TwentyFourHour)
    {
        string Time(LocalTime time) => ClockText.Format(time, clock);

        var length = Length(activity.LengthMinutes);
        if (activity.RepeatRule is null)
        {
            // When it really starts (§8): 01:30 on the night the clocks go forward is 02:30.
            var start = activity.StartDate.At(activity.StartTime);
            if (DateTimeZoneProviders.Tzdb.GetZoneOrNull(activity.TimeZoneId) is { } zone)
            {
                start = ClockTimes.ToInstant(start, zone).InZone(zone).LocalDateTime;
            }

            var skipped = start.TimeOfDay != activity.StartTime
                ? $" ({Time(activity.StartTime)} doesn't exist that night: the clocks go forward)"
                : "";
            return $"{Date.Format(start.Date)} at {Time(start.TimeOfDay)} for {length}{skipped}";
        }

        var at = $"at {Time(activity.StartTime)} for {length}";
        RecurrenceRule rule;
        try
        {
            rule = RecurrenceRuleText.Parse(activity.RepeatRule);
        }
        catch (FormatException)
        {
            // A rule edited by hand outside the app: the list still opens, so it can be fixed or deleted.
            return $"Repeats in a way the app can't read, {at}, from {Date.Format(activity.StartDate)}";
        }
        var pattern = rule.Frequency == RepeatFrequency.Daily
            ? rule.Every == 1 ? "Every day" : $"Every {rule.Every} days"
            : $"{(rule.Every == 1 ? "Every week" : $"Every {rule.Every} weeks")} on " +
              string.Join(", ", Week.Days.Where(rule.Days.Contains).Select(day => day.ToString()[..3]));
        var ending = (rule.LastDate, rule.Count) switch
        {
            ({ } lastDate, _) => $", until {Date.Format(lastDate)}",
            (_, 1) => ", once",
            (_, { } count) => $", {count} times",
            _ => "",
        };
        return $"{pattern} {at}, from {Date.Format(activity.StartDate)}{ending}";
    }

    /// <summary>For example "50 min", "8 h" or "1 h 30 min".</summary>
    public static string Length(int minutes) => (minutes / 60, minutes % 60) switch
    {
        (0, var justMinutes) => $"{justMinutes} min",
        (var hours, 0) => $"{hours} h",
        (var hours, var andMinutes) => $"{hours} h {andMinutes} min",
    };
}
