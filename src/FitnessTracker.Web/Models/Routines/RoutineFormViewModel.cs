using System.ComponentModel.DataAnnotations;
using FitnessTracker.Domain.Templates;
using FitnessTracker.Domain.Time;
using FitnessTracker.Web.Data;
using NodaTime;

namespace FitnessTracker.Web.Models.Routines;

/// <summary>CAL-9: the add and edit forms for a routine: its name, how many weeks it takes to repeat, and week A.</summary>
public class RoutineFormViewModel
{
    // Trimmed as it is bound, so the checks see exactly what will be saved.
    [Required(ErrorMessage = "Enter a name for the routine.")]
    [StringLength(Routine.NameMaxLength, ErrorMessage = "The name must be {1} characters or fewer.")]
    public string Name { get; set => field = value?.Trim() ?? ""; } = "";

    [Display(Name = "How many weeks before it repeats")]
    [Range(1, WeekCycle.MaxWeeks, ErrorMessage = "Choose how many weeks it takes to repeat, from {1} to {2}.")]
    public int CycleWeeks { get; set; } = 1;

    /// <summary>Any date in week A, as the date box sends it.</summary>
    [Display(Name = "Week A is the week of")]
    public string? WeekA { get; set; }

    [Display(Name = "This is my usual routine")]
    public bool IsUsual { get; set; }

    /// <summary>Edit only: the version of the routine the form was opened with (requirements §9).</summary>
    public Guid Version { get; set; }

    /// <summary>Q3: week A is the week it is first used, so a new routine's form starts on this week.</summary>
    public static RoutineFormViewModel ForNew(LocalDate today, IsoDayOfWeek weekStart, bool firstUsual) => new()
    {
        WeekA = FormInput.Date.Format(CalendarWeeks.StartOf(today, weekStart)),
        IsUsual = firstUsual,
    };

    public static RoutineFormViewModel From(Routine routine) => new()
    {
        Name = routine.Name,
        CycleWeeks = routine.CycleWeeks,
        WeekA = FormInput.Date.Format(routine.WeekAStarts),
        IsUsual = routine.IsUsual,
        Version = routine.Version,
    };

    /// <summary>Problems with what was typed that the attributes can't see, listed with each field's own.</summary>
    public IEnumerable<ValidationResult> ProblemsAcrossTheForm()
    {
        // Date boxes show dates the browser's way (02/11/2026 here), so the messages don't show a format.
        var weekA = FormInput.ParseDate(WeekA);
        if (weekA is null)
        {
            yield return new ValidationResult(
                string.IsNullOrWhiteSpace(WeekA) ? "Enter a date in week A." : "Enter a real date in week A, for example 2 November 2026.",
                [nameof(WeekA)]);
        }
        else if (!FormInput.IsPlannable(weekA.Value))
        {
            yield return new ValidationResult(FormInput.OutsidePlannableYears, [nameof(WeekA)]);
        }
    }

    /// <summary>
    /// Copies the form onto the routine, keeping week A as the first day of its week, with weeks starting on
    /// <paramref name="weekStart"/> (the routine's own once saved). Only call once the form is valid.
    /// </summary>
    public void ApplyTo(Routine routine, IsoDayOfWeek weekStart)
    {
        routine.Name = Name;
        routine.CycleWeeks = CycleWeeks;
        routine.WeekStart = weekStart;
        routine.WeekAStarts = CalendarWeeks.StartOf(FormInput.ParseDate(WeekA)!.Value, weekStart);
        routine.IsUsual = IsUsual;
    }
}
