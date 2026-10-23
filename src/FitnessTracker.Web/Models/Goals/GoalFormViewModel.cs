using System.ComponentModel.DataAnnotations;
using FitnessTracker.Web.Data;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace FitnessTracker.Web.Models.Goals;

/// <summary>
/// GOL-1: the add and edit forms for a weekly goal. Each session is described as an activity's form describes one
/// (category, place, length, getting ready and changing, demanding, priority), without a time: the planner finds that.
/// </summary>
public class GoalFormViewModel
{
    // Trimmed as it is bound, so the checks see exactly what will be saved.
    [Display(Name = "Name")]
    [Required(ErrorMessage = "Enter a name for the goal.")]
    [StringLength(WeeklyGoal.TitleMaxLength, ErrorMessage = "The name must be {1} characters or fewer.")]
    public string Title { get; set => field = value?.Trim() ?? ""; } = "";

    public ActivityCategory Category { get; set; } = ActivityCategory.Gym;

    [Display(Name = "Where")]
    public Guid? LocationId { get; set; }

    [Display(Name = "Times a week")]
    [Range(1, WeeklyGoal.MaxSessionsPerWeek, ErrorMessage = "Enter how many times a week, from {1} to {2}.")]
    public int SessionsPerWeek { get; set; } = 3;

    [Display(Name = "Hours")]
    [Range(0, 24, ErrorMessage = "Enter between {1} and {2} hours.")]
    public int DurationHours { get; set; } = 1;

    [Display(Name = "Minutes")]
    [Range(0, 59, ErrorMessage = "Enter between {1} and {2} minutes.")]
    public int DurationMinutes { get; set; }

    /// <summary>Blank: the category's default (ACC-6), filled in when it is saved.</summary>
    [Display(Name = "Getting ready beforehand (minutes)")]
    [Range(0, Activity.MaxBufferMinutes, ErrorMessage = "Enter between {1} and {2} minutes.")]
    public int? PreparationMinutes { get; set; }

    /// <summary>Blank: the category's default (ACC-6), filled in when it is saved.</summary>
    [Display(Name = "Changing or showering afterwards (minutes)")]
    [Range(0, Activity.MaxBufferMinutes, ErrorMessage = "Enter between {1} and {2} minutes.")]
    public int? ChangingMinutes { get; set; }

    public ActivityPriority Priority { get; set; } = ActivityPriority.Normal;

    [Display(Name = "Demanding: a hard session that needs recovery afterwards")]
    public bool IsDemanding { get; set; }

    /// <summary>One entry per weekday, Monday first, true for the days preferred. None: any day.</summary>
    public List<bool> PreferredDays { get; set; } = Week.Days.Select(_ => false).ToList();

    [Display(Name = "Time of day")]
    public PreferredTimeOfDay PreferredTimeOfDay { get; set; }

    /// <summary>Edit only: the version of the goal the form was opened with (requirements §9).</summary>
    public Guid Version { get; set; }

    /// <summary>The signed-in person's locations, for the list. Filled by the controller, never posted.</summary>
    [BindNever]
    [ValidateNever]
    public IReadOnlyList<Location> Locations { get; set; } = [];

    /// <summary>ACC-6: each category's default buffers, for the hint in the empty boxes. Filled by the controller, never posted.</summary>
    [BindNever]
    [ValidateNever]
    public IReadOnlyDictionary<ActivityCategory, (int Preparation, int Changing)> DefaultBuffers { get; set; } =
        Enum.GetValues<ActivityCategory>().ToDictionary(category => category, CategoryBuffers.BuiltIn);

    public static GoalFormViewModel From(WeeklyGoal goal) => new()
    {
        Title = goal.Title,
        Category = goal.Category,
        LocationId = goal.LocationId,
        SessionsPerWeek = goal.SessionsPerWeek,
        DurationHours = goal.SessionMinutes / 60,
        DurationMinutes = goal.SessionMinutes % 60,
        PreparationMinutes = goal.PreparationMinutes,
        ChangingMinutes = goal.ChangingMinutes,
        Priority = goal.Priority,
        IsDemanding = goal.IsDemanding,
        PreferredDays = Week.Days.Select(day => Week.DaysIn(goal.PreferredDays).Contains(day)).ToList(),
        PreferredTimeOfDay = goal.PreferredTimeOfDay,
        Version = goal.Version,
    };

    /// <summary>Problems with what was typed that the attributes can't see, listed with each field's own.</summary>
    public IEnumerable<ValidationResult> ProblemsAcrossTheForm()
    {
        var length = (DurationHours * 60) + DurationMinutes;
        if (DurationHours is >= 0 and <= 24 && DurationMinutes is >= 0 and <= 59 && length is < 1 or > WeeklyGoal.MaxSessionMinutes)
        {
            yield return new ValidationResult("Enter how long each time takes: at least 1 minute and at most 24 hours.", [nameof(DurationHours)]);
        }

        if (PreferredDays.Count != Week.Days.Count)
        {
            yield return new ValidationResult("Choose the days you'd prefer, or none for any day.", [$"{nameof(PreferredDays)}[0]"]);
        }
    }

    /// <summary>Copies the form onto the goal, with the category's default for a buffer left blank (ACC-6). Only once valid.</summary>
    public void ApplyTo(WeeklyGoal goal)
    {
        var (defaultPreparation, defaultChanging) = DefaultBuffers.TryGetValue(Category, out var defaults) ? defaults : (0, 0);
        goal.Title = Title;
        goal.Category = Category;
        goal.LocationId = LocationId;
        goal.SessionsPerWeek = SessionsPerWeek;
        goal.SessionMinutes = (DurationHours * 60) + DurationMinutes;
        goal.PreparationMinutes = PreparationMinutes ?? defaultPreparation;
        goal.ChangingMinutes = ChangingMinutes ?? defaultChanging;
        goal.Priority = Priority;
        goal.IsDemanding = IsDemanding;
        goal.PreferredDays = Week.SetOf(Week.Days.Where((_, i) => PreferredDays[i]));
        goal.PreferredTimeOfDay = PreferredTimeOfDay;
    }
}
