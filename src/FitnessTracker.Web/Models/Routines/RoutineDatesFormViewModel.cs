using System.ComponentModel.DataAnnotations;
using FitnessTracker.Web.Data;

namespace FitnessTracker.Web.Models.Routines;

/// <summary>CAL-9, Q14: the add and change forms for a routine chosen for some dates (a holiday week).</summary>
public class RoutineDatesFormViewModel
{
    [Display(Name = "Routine")]
    public Guid? RoutineId { get; set; }

    /// <summary>The first date, as the date box sends it.</summary>
    [Display(Name = "First date")]
    public string? From { get; set; }

    /// <summary>The last date, inclusive, as the date box sends it.</summary>
    [Display(Name = "Last date")]
    public string? To { get; set; }

    /// <summary>Change only: the version the form was opened with (requirements §9).</summary>
    public Guid Version { get; set; }

    /// <summary>Filled by the controller, never posted: the user's routines to choose from, usual first.</summary>
    public IReadOnlyList<Routine> Routines { get; set; } = [];

    public static RoutineDatesFormViewModel For(RoutineDateRange chosen) => new()
    {
        RoutineId = chosen.RoutineId,
        From = FormInput.Date.Format(chosen.StartDate),
        To = FormInput.Date.Format(chosen.EndDate),
        Version = chosen.Version,
    };

    /// <summary>
    /// Problems with what was typed, listed with each field's own. Whether the routine is the user's, and the dates
    /// overlap others, are for the controller, which can read them.
    /// </summary>
    public IEnumerable<ValidationResult> Problems()
    {
        if (RoutineId is null || RoutineId == Guid.Empty)
        {
            yield return new ValidationResult("Choose a routine for these dates.", [nameof(RoutineId)]);
        }

        // Date boxes show dates the browser's way (02/11/2026 here), so the messages don't show a format.
        var from = FormInput.ParseDate(From);
        var to = FormInput.ParseDate(To);
        if (from is null)
        {
            yield return new ValidationResult(
                string.IsNullOrWhiteSpace(From) ? "Enter the first date." : "Enter a real first date, for example 26 December 2026.",
                [nameof(From)]);
        }
        else if (!FormInput.IsPlannable(from.Value))
        {
            yield return new ValidationResult(FormInput.OutsidePlannableYears, [nameof(From)]);
        }

        if (to is null)
        {
            yield return new ValidationResult(
                string.IsNullOrWhiteSpace(To) ? "Enter the last date." : "Enter a real last date, for example 3 January 2027.",
                [nameof(To)]);
        }
        else if (!FormInput.IsPlannable(to.Value))
        {
            yield return new ValidationResult(FormInput.OutsidePlannableYears, [nameof(To)]);
        }
        else if (from is { } first && to < first)
        {
            yield return new ValidationResult("The last date can't be before the first.", [nameof(To)]);
        }
    }

    /// <summary>Copies the form onto the dates. Only call once the form is valid.</summary>
    public void ApplyTo(RoutineDateRange chosen)
    {
        chosen.RoutineId = RoutineId!.Value;
        chosen.StartDate = FormInput.ParseDate(From)!.Value;
        chosen.EndDate = FormInput.ParseDate(To)!.Value;
    }
}
