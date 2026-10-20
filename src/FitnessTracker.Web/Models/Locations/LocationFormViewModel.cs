using System.ComponentModel.DataAnnotations;
using FitnessTracker.Web.Data;
using NodaTime;
using NodaTime.Text;

namespace FitnessTracker.Web.Models.Locations;

/// <summary>The add and edit forms for a location (TRV-1): name, whether it is home, and opening hours.</summary>
public class LocationFormViewModel
{
    private static readonly LocalTimePattern ClockTime = LocalTimePattern.CreateWithInvariantCulture("HH:mm");

    // Trimmed as it is bound, so the checks below see exactly what will be saved
    // (phone keyboards add a space after an autocompleted word).
    [Required(ErrorMessage = "Enter a name for the location.")]
    [StringLength(Location.NameMaxLength, ErrorMessage = "The name must be {1} characters or fewer.")]
    public string Name { get; set => field = value?.Trim() ?? ""; } = "";

    [Display(Name = "This is my home")]
    public bool IsHome { get; set; }

    public bool HasOpeningHours { get; set; }

    /// <summary>One entry per weekday, Monday first. Only used when <see cref="HasOpeningHours"/> is true.</summary>
    public List<OpeningDayFormViewModel> OpeningDays { get; set; } = Week.Days.Select(day => new OpeningDayFormViewModel { Day = day }).ToList();

    /// <summary>Edit only: the version of the location the form was opened with (requirements §9).</summary>
    public Guid Version { get; set; }

    public static LocationFormViewModel From(Location location) => new()
    {
        Name = location.Name,
        IsHome = location.IsHome,
        HasOpeningHours = location.HasOpeningHours,
        OpeningDays = Week.Days.Select(day => location.OpeningTimes.FirstOrDefault(time => time.Day == day) is { } time
                ? new OpeningDayFormViewModel { Day = day, Opens = ClockTime.Format(time.Opens), Closes = ClockTime.Format(time.Closes) }
                : new OpeningDayFormViewModel { Day = day, Closed = location.HasOpeningHours })
            .ToList(),
        Version = location.Version,
    };

    /// <summary>
    /// Problems the attributes can't see: an open day needs two real times, and set opening hours need at least one
    /// open day. The controller runs these every time, so every problem is listed at once.
    /// </summary>
    public IEnumerable<ValidationResult> ProblemsAcrossTheForm()
    {
        if (!HasOpeningHours)
        {
            yield break;
        }

        if (OpeningDays.Count != Week.Days.Count || OpeningDays.Select(day => day.Day).Distinct().Count() != Week.Days.Count
            || OpeningDays.Any(day => !Week.Days.Contains(day.Day)))
        {
            yield return new ValidationResult("Enter the opening hours for each day of the week.");
            yield break;
        }

        for (var i = 0; i < OpeningDays.Count; i++)
        {
            var day = OpeningDays[i];
            if (day.Closed)
            {
                continue;
            }

            foreach (var (field, value, what) in new[] { ("Opens", day.Opens, "opening"), ("Closes", day.Closes, "closing") })
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    yield return new ValidationResult($"Enter the {what} time for {day.Day}, or mark it closed.", [$"OpeningDays[{i}].{field}"]);
                }
                else if (!ClockTime.Parse(value.Trim()).Success)
                {
                    yield return new ValidationResult($"Enter the {what} time for {day.Day} as a time between 00:00 and 23:59, for example 06:00.", [$"OpeningDays[{i}].{field}"]);
                }
            }
        }

        if (OpeningDays.All(day => day.Closed))
        {
            yield return new ValidationResult(
                "Every day is marked closed. Enter the hours for at least one day, or choose \"No set opening hours\".",
                [nameof(HasOpeningHours)]);
        }
    }

    /// <summary>The opening times to store: one per open day, or none without set hours. Only call once the form is valid.</summary>
    public IEnumerable<LocationOpeningTime> OpeningTimesToStore() =>
        HasOpeningHours
            ? OpeningDays.Where(day => !day.Closed).Select(day => new LocationOpeningTime
            {
                Day = day.Day,
                Opens = ClockTime.Parse(day.Opens!.Trim()).Value,
                Closes = ClockTime.Parse(day.Closes!.Trim()).Value,
            })
            : [];
}

/// <summary>One weekday's row on the location form.</summary>
public class OpeningDayFormViewModel
{
    public IsoDayOfWeek Day { get; set; }

    public bool Closed { get; set; }

    public string? Opens { get; set; }

    public string? Closes { get; set; }
}
