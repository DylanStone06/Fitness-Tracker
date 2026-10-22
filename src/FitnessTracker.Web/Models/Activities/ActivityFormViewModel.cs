using System.ComponentModel.DataAnnotations;
using FitnessTracker.Domain.Recurrence;
using FitnessTracker.Web.Data;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using NodaTime;

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
public class ActivityFormViewModel : IConfirmsClashes
{
    public const int MaxRepeatEvery = 99;

    public const int MaxEndCount = 500;


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

    /// <summary>Opened from the calendar: saving (or cancelling) goes back to it, on the activity's week.</summary>
    public bool ReturnToCalendar { get; set; }

    /// <summary>Opened from "Plan my week" (SCH-4): saving (or cancelling) goes back to the plan, on the activity's week.</summary>
    public bool ReturnToPlan { get; set; }

    /// <summary>GOL-1: the weekly goal it is for, carried by the page, or null.</summary>
    public Guid? GoalId { get; set; }

    /// <summary>The start date as saved, once the form is valid: the week the calendar goes back to.</summary>
    public string? SavedStartDate => FormInput.ParseDate(StartDate) is { } date ? FormInput.Date.Format(date) : null;

    /// <summary>The signed-in person's locations, for the list. Filled by the controller, never posted.</summary>
    [BindNever]
    [ValidateNever]
    public IReadOnlyList<Location> Locations { get; set; } = [];

    /// <summary>
    /// A new activity's form, starting at the date and time selected on the calendar. Either is left blank when it
    /// can't be read (a link edited by hand), as the form is without them.
    /// </summary>
    public static ActivityFormViewModel ForNew(string? date, string? time, bool returnToCalendar) => new()
    {
        StartDate = FormInput.ParseDate(date) is { } start && FormInput.IsPlannable(start) ? FormInput.Date.Format(start) : null,
        StartTime = FormInput.ParseTime(time) is { } clock ? FormInput.Time.Format(clock) : null,
        ReturnToCalendar = returnToCalendar,
    };

    /// <summary>
    /// SCH-4: a session of a weekly goal at a suggested date and time, filled in from the goal, to change before saving:
    /// movable (the planner chose the time), and every week on that day when <paramref name="everyWeek"/> (Q12).
    /// </summary>
    public static ActivityFormViewModel ForGoal(WeeklyGoal goal, string? date, string? time, bool everyWeek, bool returnToPlan)
    {
        var model = ForNew(date, time, returnToCalendar: false);
        model.Title = goal.Title;
        model.Category = goal.Category;
        model.LocationId = goal.LocationId;
        (model.DurationHours, model.DurationMinutes) = (goal.SessionMinutes / 60, goal.SessionMinutes % 60);
        model.IsFixed = false;
        model.Priority = goal.Priority;
        model.IsDemanding = goal.IsDemanding;
        model.PreparationMinutes = goal.PreparationMinutes;
        model.ChangingMinutes = goal.ChangingMinutes;
        model.GoalId = goal.Id;
        model.ReturnToPlan = returnToPlan;
        if (everyWeek && FormInput.ParseDate(model.StartDate) is { } day)
        {
            model.Repeats = RepeatChoice.Weekly;
            model.RepeatDays = Week.Days.Select(weekday => weekday == day.DayOfWeek).ToList();
        }

        return model;
    }

    /// <summary>CAL-8: a one-off dragged on the calendar: its new start and length in the boxes, to check and save.</summary>
    public void FillDragged(string? start, int? minutes)
    {
        if (FormInput.Dragged(start, minutes) is { } dragged)
        {
            StartDate = FormInput.Date.Format(dragged.Start.Date);
            StartTime = FormInput.Time.Format(dragged.Start.TimeOfDay);
            (DurationHours, DurationMinutes) = (dragged.Minutes / 60, dragged.Minutes % 60);
        }
    }

    public static ActivityFormViewModel From(Activity activity)
    {
        var model = new ActivityFormViewModel
        {
            Title = activity.Title,
            Category = activity.Category,
            LocationId = activity.LocationId,
            StartDate = FormInput.Date.Format(activity.StartDate),
            StartTime = FormInput.Time.Format(activity.StartTime),
            DurationHours = activity.LengthMinutes / 60,
            DurationMinutes = activity.LengthMinutes % 60,
            IsFixed = activity.IsFixed,
            GoalId = activity.GoalId,
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
                ({ } lastDate, _) => (RepeatEndChoice.OnDate, FormInput.Date.Format(lastDate), (int?)null),
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
        var startDate = FormInput.ParseDate(StartDate);
        if (startDate is null)
        {
            yield return new ValidationResult(
                string.IsNullOrWhiteSpace(StartDate) ? "Enter the date it happens, or the first date if it repeats." : "Enter a real date, for example 2 November 2026.",
                [nameof(StartDate)]);
        }
        else if (!FormInput.IsPlannable(startDate.Value))
        {
            yield return new ValidationResult(FormInput.OutsidePlannableYears, [nameof(StartDate)]);
        }

        if (FormInput.ParseTime(StartTime) is null)
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
        activity.StartDate = FormInput.ParseDate(StartDate)!.Value;
        activity.StartTime = FormInput.ParseTime(StartTime)!.Value;
        activity.LengthMinutes = (DurationHours * 60) + DurationMinutes;
        activity.IsFixed = IsFixed;
        activity.Priority = Priority;
        activity.IsDemanding = IsDemanding;
        activity.PreparationMinutes = PreparationMinutes ?? defaultPreparation;
        activity.ChangingMinutes = ChangingMinutes ?? defaultChanging;
        activity.Notes = Notes;
        activity.GoalId = GoalId;
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
            var endDate = FormInput.ParseDate(EndDate);
            if (endDate is null)
            {
                problems.Add(new ValidationResult(
                    string.IsNullOrWhiteSpace(EndDate) ? "Enter the last date it can happen." : "Enter a real last date, for example 31 December 2026.",
                    [nameof(EndDate)]));
            }
            else if (!FormInput.IsPlannable(endDate.Value))
            {
                problems.Add(new ValidationResult(FormInput.OutsidePlannableYears, [nameof(EndDate)]));
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
            RepeatEndChoice.OnDate => rule?.EndingOn(FormInput.ParseDate(EndDate)!.Value),
            RepeatEndChoice.AfterCount => rule?.EndingAfter(EndCount!.Value),
            _ => rule,
        };
    }

}

