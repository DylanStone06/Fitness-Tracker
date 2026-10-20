using System.ComponentModel.DataAnnotations;
using FitnessTracker.Domain.Recurrence;
using FitnessTracker.Web.Data;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using NodaTime;
using NodaTime.Text;

namespace FitnessTracker.Web.Models.Activities;

/// <summary>CAL-3: whether and how an activity repeats, as the form offers it.</summary>
public enum RepeatChoice
{
    Never,
    Daily,
    Weekly,
}

/// <summary>CAL-3: how a repeat ends, as the form offers it.</summary>
public enum RepeatEndChoice
{
    Never,
    OnDate,
    AfterCount,
}

/// <summary>
/// The add and edit forms for an activity (CAL-2, CAL-3, FIT-6). The length is asked for as hours and minutes of
/// elapsed time (§8), which is what is stored, so an activity that runs past midnight needs nothing special.
/// </summary>
public class ActivityFormViewModel
{
    public const int MaxRepeatEvery = 99;

    public const int MaxEndCount = 500;

    // Years the app plans in. Anything outside is almost certainly a mistyped year, and keeps date maths well away
    // from the end of the calendar (31 December 9999).
    private const int FirstPlannableYear = 1900;

    private const int LastPlannableYear = 2999;

    private const string OutsidePlannableYears = "Enter a date between the years 1900 and 2999.";

    // As <input type="date"> and <input type="time"> send them.
    private static readonly LocalDatePattern DateInput = LocalDatePattern.Iso;

    private static readonly LocalTimePattern TimeInput = LocalTimePattern.CreateWithInvariantCulture("HH:mm");

    // Trimmed as it is bound, so the checks see exactly what will be saved.
    [Required(ErrorMessage = "Enter a title for the activity.")]
    [StringLength(Activity.TitleMaxLength, ErrorMessage = "The title must be {1} characters or fewer.")]
    public string Title { get; set => field = value?.Trim() ?? ""; } = "";

    public ActivityCategory Category { get; set; } = ActivityCategory.Other;

    [Display(Name = "Where")]
    public Guid? LocationId { get; set; }

    [Display(Name = "Date")]
    public string? StartDate { get; set; }

    [Display(Name = "Start time")]
    public string? StartTime { get; set; }

    // Up to 25: the whole day the clocks go back is allowed (requirements Q7); the check on save refuses 25 hours on
    // any other day.
    [Display(Name = "Hours")]
    [Range(0, 25, ErrorMessage = "Enter between {1} and {2} hours.")]
    public int DurationHours { get; set; } = 1;

    [Display(Name = "Minutes")]
    [Range(0, 59, ErrorMessage = "Enter between {1} and {2} minutes.")]
    public int DurationMinutes { get; set; }

    public bool IsFixed { get; set; } = true;

    public ActivityPriority Priority { get; set; } = ActivityPriority.Normal;

    [Display(Name = "Demanding: a hard session that needs recovery afterwards")]
    public bool IsDemanding { get; set; }

    /// <summary>Blank: the category's default (ACC-6), filled in when it is saved.</summary>
    [Display(Name = "Getting ready beforehand (minutes)")]
    [Range(0, Activity.MaxBufferMinutes, ErrorMessage = "Enter between {1} and {2} minutes.")]
    public int? PreparationMinutes { get; set; }

    /// <summary>Blank: the category's default (ACC-6), filled in when it is saved.</summary>
    [Display(Name = "Changing or showering afterwards (minutes)")]
    [Range(0, Activity.MaxBufferMinutes, ErrorMessage = "Enter between {1} and {2} minutes.")]
    public int? ChangingMinutes { get; set; }

    /// <summary>ACC-6: each category's default buffers, for the hint in the empty boxes. Filled by the controller, never posted.</summary>
    [BindNever]
    [ValidateNever]
    public IReadOnlyDictionary<ActivityCategory, (int Preparation, int Changing)> DefaultBuffers { get; set; } =
        Enum.GetValues<ActivityCategory>().ToDictionary(category => category, CategoryBuffers.BuiltIn);

    [StringLength(Activity.NotesMaxLength, ErrorMessage = "Notes must be {1} characters or fewer.")]
    public string? Notes { get; set => field = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); }

    public RepeatChoice Repeats { get; set; }

    [Display(Name = "Every")]
    public int RepeatEvery { get; set; } = 1;

    /// <summary>Weekly only: one entry per weekday, Monday first, true for the days it repeats on.</summary>
    public List<bool> RepeatDays { get; set; } = Week.Days.Select(_ => false).ToList();

    public RepeatEndChoice Ends { get; set; }

    [Display(Name = "Last date")]
    public string? EndDate { get; set; }

    [Display(Name = "Number of times")]
    public int? EndCount { get; set; }

    /// <summary>
    /// Q2: the user has seen the clashes with other activities and wants to keep them (§7.2: an accepted conflict).
    /// Only counts for the clashes in <see cref="ShownClashes"/>, so a clash is always a decision made with it in view.
    /// </summary>
    [Display(Name = "Save anyway, with these clashes")]
    public bool ConfirmClashes { get; set; }

    /// <summary>A fingerprint of the clashes the page showed, so "Save anyway" accepts exactly those and no others.</summary>
    public string? ShownClashes { get; set; }

    /// <summary>Edit only: the version of the activity the form was opened with (requirements §9).</summary>
    public Guid Version { get; set; }

    /// <summary>The signed-in person's locations, for the list. Filled by the controller, never posted.</summary>
    [BindNever]
    [ValidateNever]
    public IReadOnlyList<Location> Locations { get; set; } = [];

    public static ActivityFormViewModel From(Activity activity)
    {
        var model = new ActivityFormViewModel
        {
            Title = activity.Title,
            Category = activity.Category,
            LocationId = activity.LocationId,
            StartDate = DateInput.Format(activity.StartDate),
            StartTime = TimeInput.Format(activity.StartTime),
            DurationHours = activity.LengthMinutes / 60,
            DurationMinutes = activity.LengthMinutes % 60,
            IsFixed = activity.IsFixed,
            Priority = activity.Priority,
            IsDemanding = activity.IsDemanding,
            PreparationMinutes = activity.PreparationMinutes,
            ChangingMinutes = activity.ChangingMinutes,
            Notes = activity.Notes,
            Version = activity.Version,
        };

        if (activity.RepeatRule is { } text)
        {
            var rule = RecurrenceRuleText.Parse(text);
            model.Repeats = rule.Frequency == RepeatFrequency.Daily ? RepeatChoice.Daily : RepeatChoice.Weekly;
            model.RepeatEvery = rule.Every;
            model.RepeatDays = Week.Days.Select(rule.Days.Contains).ToList();
            (model.Ends, model.EndDate, model.EndCount) = (rule.LastDate, rule.Count) switch
            {
                ({ } lastDate, _) => (RepeatEndChoice.OnDate, DateInput.Format(lastDate), (int?)null),
                (_, { } count) => (RepeatEndChoice.AfterCount, null, count),
                _ => (RepeatEndChoice.Never, null, null),
            };
        }

        return model;
    }

    /// <summary>
    /// Problems with what was typed that the attributes can't see, listed with each field's own so every problem shows
    /// at once. The repeat is only checked when it repeats. Whether the activity fits (it happens at all, opening
    /// hours, travel, clashes) is the one check's job (SCH-1, ActivityChecker), run once the form is valid.
    /// </summary>
    public IEnumerable<ValidationResult> ProblemsAcrossTheForm()
    {
        // The choices (category, priority, repeats, ends) need no check here: the model binder refuses a value that
        // isn't one of them, even a number, and leaves the default.
        // Date boxes show dates the browser's way (02/11/2026 here), so the messages don't show a format.
        var startDate = ParseDate(StartDate);
        if (startDate is null)
        {
            yield return new ValidationResult(
                string.IsNullOrWhiteSpace(StartDate) ? "Enter the date it happens, or the first date if it repeats." : "Enter a real date, for example 2 November 2026.",
                [nameof(StartDate)]);
        }
        else if (!IsPlannable(startDate.Value))
        {
            yield return new ValidationResult(OutsidePlannableYears, [nameof(StartDate)]);
        }

        if (ParseTime(StartTime) is null)
        {
            yield return new ValidationResult(
                string.IsNullOrWhiteSpace(StartTime) ? "Enter the start time." : "Enter the start time as a time between 00:00 and 23:59, for example 07:00.",
                [nameof(StartTime)]);
        }

        var length = (DurationHours * 60) + DurationMinutes;
        if (DurationHours is >= 0 and <= 25 && DurationMinutes is >= 0 and <= 59 && length is < 1 or > 25 * 60)
        {
            yield return new ValidationResult(
                "Enter how long it takes: at least 1 minute and at most 25 hours (more than 24 only fits on a day the clocks go back).", [nameof(DurationHours)]);
        }

        if (Repeats == RepeatChoice.Never)
        {
            yield break;
        }

        foreach (var problem in RepeatProblems())
        {
            yield return problem;
        }
    }

    /// <summary>
    /// Copies the form onto the activity, with the category's default for a buffer left blank (ACC-6). Only call once
    /// the form is valid.
    /// </summary>
    public void ApplyTo(Activity activity, IsoDayOfWeek weekStart)
    {
        var (defaultPreparation, defaultChanging) = DefaultBuffers.TryGetValue(Category, out var defaults) ? defaults : (0, 0);
        activity.Title = Title;
        activity.Category = Category;
        activity.LocationId = LocationId;
        activity.StartDate = ParseDate(StartDate)!.Value;
        activity.StartTime = ParseTime(StartTime)!.Value;
        activity.LengthMinutes = (DurationHours * 60) + DurationMinutes;
        activity.IsFixed = IsFixed;
        activity.Priority = Priority;
        activity.IsDemanding = IsDemanding;
        activity.PreparationMinutes = PreparationMinutes ?? defaultPreparation;
        activity.ChangingMinutes = ChangingMinutes ?? defaultChanging;
        activity.Notes = Notes;
        activity.RepeatRule = Repeats == RepeatChoice.Never ? null : RecurrenceRuleText.Format(Rule(weekStart)!);
    }

    private List<ValidationResult> RepeatProblems()
    {
        var problems = new List<ValidationResult>();
        if (RepeatEvery is < 1 or > MaxRepeatEvery)
        {
            problems.Add(new ValidationResult($"Enter how often it repeats as a number from 1 to {MaxRepeatEvery}.", [nameof(RepeatEvery)]));
        }

        if (Repeats == RepeatChoice.Weekly)
        {
            if (RepeatDays.Count != Week.Days.Count)
            {
                problems.Add(new ValidationResult("Choose the days of the week it repeats on.", [$"{nameof(RepeatDays)}[0]"]));
            }
            else if (!RepeatDays.Contains(true))
            {
                problems.Add(new ValidationResult("Choose at least one day of the week for it to repeat on.", [$"{nameof(RepeatDays)}[0]"]));
            }
        }

        if (Ends == RepeatEndChoice.OnDate)
        {
            var endDate = ParseDate(EndDate);
            if (endDate is null)
            {
                problems.Add(new ValidationResult(
                    string.IsNullOrWhiteSpace(EndDate) ? "Enter the last date it can happen." : "Enter a real last date, for example 31 December 2026.",
                    [nameof(EndDate)]));
            }
            else if (!IsPlannable(endDate.Value))
            {
                problems.Add(new ValidationResult(OutsidePlannableYears, [nameof(EndDate)]));
            }
        }

        if (Ends == RepeatEndChoice.AfterCount && EndCount is not (>= 1 and <= MaxEndCount))
        {
            problems.Add(new ValidationResult($"Enter how many times it happens as a number from 1 to {MaxEndCount}.", [nameof(EndCount)]));
        }

        return problems;
    }

    // The rule the form describes, or null for a one-off. Only call once the repeat fields are valid.
    private RecurrenceRule? Rule(IsoDayOfWeek weekStart)
    {
        var rule = Repeats switch
        {
            RepeatChoice.Daily => RecurrenceRule.Daily(RepeatEvery),
            RepeatChoice.Weekly => RecurrenceRule.Weekly(Week.Days.Where((_, i) => RepeatDays[i]), RepeatEvery, weekStart),
            _ => null,
        };

        return Ends switch
        {
            RepeatEndChoice.OnDate => rule?.EndingOn(ParseDate(EndDate)!.Value),
            RepeatEndChoice.AfterCount => rule?.EndingAfter(EndCount!.Value),
            _ => rule,
        };
    }

    private static bool IsPlannable(LocalDate date) => date.Year is >= FirstPlannableYear and <= LastPlannableYear;

    private static LocalDate? ParseDate(string? text) =>
        text is not null && DateInput.Parse(text.Trim()) is { Success: true } result ? result.Value : null;

    private static LocalTime? ParseTime(string? text) =>
        text is not null && TimeInput.Parse(text.Trim()) is { Success: true } result ? result.Value : null;
}
