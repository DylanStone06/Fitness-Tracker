using System.ComponentModel.DataAnnotations;
using FitnessTracker.Domain.Recurrence;
using FitnessTracker.Domain.Time;
using FitnessTracker.Web.Data;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using NodaTime;
using NodaTime.Text;

namespace FitnessTracker.Web.Models.Activities;

/// <summary>CAL-4: what to do with one occurrence: the button pressed.</summary>
public enum OccurrenceChangeChoice
{
    Move,
    Cancel,
    Restore,
}

/// <summary>
/// CAL-4: the page for one occurrence of a repeating activity. It is known by <see cref="Date"/>, the date its rule
/// puts it on, which stays the same after a move. A move is a new date, clock time and elapsed length (§8).
/// </summary>
public class OccurrenceFormViewModel : IConfirmsClashes
{
    private static readonly LocalDatePattern Named = LocalDatePattern.CreateWithInvariantCulture("ddd d MMM uuuu");

    /// <summary>The occurrence's own date ("2026-11-04"), carried by the page.</summary>
    public string? Date { get; set; }

    public OccurrenceChangeChoice Change { get; set; }

    [Display(Name = "Date")]
    public string? NewDate { get; set; }

    [Display(Name = "Start time")]
    public string? NewStartTime { get; set; }

    [Display(Name = "Hours")]
    [Range(0, 25, ErrorMessage = "Enter between {1} and {2} hours.")]
    public int DurationHours { get; set; }

    [Display(Name = "Minutes")]
    [Range(0, 59, ErrorMessage = "Enter between {1} and {2} minutes.")]
    public int DurationMinutes { get; set; }

    /// <summary>The version of the activity the page was opened with (requirements §9).</summary>
    public Guid Version { get; set; }

    public bool ReturnToCalendar { get; set; }

    [Display(Name = "Save anyway, with these clashes")]
    public bool ConfirmClashes { get; set; }

    public string? ShownClashes { get; set; }

    /// <summary>Filled by the controller, never posted: the activity, how it usually happens, and this occurrence now.</summary>
    [BindNever]
    [ValidateNever]
    public Activity Activity { get; set; } = null!;

    [BindNever]
    [ValidateNever]
    public string Usually { get; set; } = "";

    [BindNever]
    [ValidateNever]
    public string Now { get; set; } = "";

    [BindNever]
    [ValidateNever]
    public bool IsChanged { get; set; }

    /// <summary>
    /// The new start, once the form is valid. Never validated: MVC's validation reads every property of a posted model,
    /// and this one has no value until the date and time are right.
    /// </summary>
    [ValidateNever]
    public LocalDateTime NewStart => FormInput.ParseDate(NewDate)!.Value.At(FormInput.ParseTime(NewStartTime)!.Value);

    [ValidateNever]
    public Duration NewLength => Duration.FromMinutes((DurationHours * 60) + DurationMinutes);

    /// <summary>
    /// The page for the occurrence on <paramref name="date"/>, its boxes filled with where it is now (or, if it is
    /// cancelled, where it usually is).
    /// </summary>
    public static OccurrenceFormViewModel For(Activity activity, Series series, LocalDate date, bool returnToCalendar, ClockFormat clock)
    {
        var model = new OccurrenceFormViewModel
        {
            Date = FormInput.Date.Format(date),
            Version = activity.Version,
            ReturnToCalendar = returnToCalendar,
        };
        var (start, length) = series.Changes.TryGetValue(date, out var change) && change is OccurrenceChange.Moved move
            ? (move.Start, move.Length)
            : (date.At(series.StartTime), series.Length);
        model.FillBoxes(start, length);
        return model.Describe(activity, series, date, clock);
    }

    /// <summary>Fills in the details shown with the form, which aren't posted, with times as the user chose (ACC-3).</summary>
    public OccurrenceFormViewModel Describe(Activity activity, Series series, LocalDate date, ClockFormat clock)
    {
        Activity = activity;
        Usually = ActivityDisplay.When(activity, clock);
        var usual = $"{Named.Format(date)} at {ClockText.Format(series.StartTime, clock)} for {Length(series.Length)}";
        (Now, IsChanged) = series.Changes.TryGetValue(date, out var change)
            ? change switch
            {
                OccurrenceChange.Moved move => ($"Moved to {Named.Format(move.Start.Date)} at {ClockText.Format(move.Start.TimeOfDay, clock)} for {Length(move.Length)} (usually {usual})", true),
                _ => ($"Cancelled (usually {usual})", true),
            }
            : (usual, false);
        return this;
    }

    /// <summary>CAL-8: a time dragged to on the calendar ("2026-11-05T18:30" for 45 minutes), when it can be read.</summary>
    public void FillDragged(string? start, int? minutes)
    {
        if (FormInput.Dragged(start, minutes) is { } dragged)
        {
            FillBoxes(dragged.Start, Duration.FromMinutes(dragged.Minutes));
        }
    }

    /// <summary>Problems with a move as typed. Cancelling and putting back need nothing typed.</summary>
    public IEnumerable<ValidationResult> ProblemsAcrossTheForm()
    {
        if (Change != OccurrenceChangeChoice.Move)
        {
            yield break;
        }

        var newDate = FormInput.ParseDate(NewDate);
        if (newDate is null)
        {
            yield return new ValidationResult(
                string.IsNullOrWhiteSpace(NewDate) ? "Enter the date to move it to." : "Enter a real date, for example 5 November 2026.",
                [nameof(NewDate)]);
        }
        else if (!FormInput.IsPlannable(newDate.Value))
        {
            yield return new ValidationResult(FormInput.OutsidePlannableYears, [nameof(NewDate)]);
        }

        if (FormInput.ParseTime(NewStartTime) is null)
        {
            yield return new ValidationResult(
                string.IsNullOrWhiteSpace(NewStartTime) ? "Enter the start time." : "Enter the start time as a time between 00:00 and 23:59, for example 07:00.",
                [nameof(NewStartTime)]);
        }

        var minutes = (DurationHours * 60) + DurationMinutes;
        if (DurationHours is >= 0 and <= 25 && DurationMinutes is >= 0 and <= 59 && minutes is < 1 or > 25 * 60)
        {
            yield return new ValidationResult(
                "Enter how long it takes: at least 1 minute and at most 25 hours (more than 24 only fits on a day the clocks go back).", [nameof(DurationHours)]);
        }
    }

    private void FillBoxes(LocalDateTime start, Duration length)
    {
        NewDate = FormInput.Date.Format(start.Date);
        NewStartTime = FormInput.Time.Format(start.TimeOfDay);
        var minutes = (int)length.TotalMinutes;
        (DurationHours, DurationMinutes) = (minutes / 60, minutes % 60);
    }

    private static string Length(Duration length) => ActivityDisplay.Length((int)length.TotalMinutes);
}
