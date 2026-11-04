using System.ComponentModel.DataAnnotations;
using FitnessTracker.Web.Data;
using FitnessTracker.Web.Models.Activities;

namespace FitnessTracker.Web.Models.Preferences;

/// <summary>ACC-6: the form for default getting-ready and changing times, one row per category, in category order.</summary>
public class CategoryBuffersFormViewModel
{
    public List<CategoryBuffersRow> Rows { get; set; } = [];

    /// <summary>Every category, with the user's own defaults where they have saved some and the built-in ones otherwise.</summary>
    public static CategoryBuffersFormViewModel From(IReadOnlyCollection<CategoryBuffers> stored) => new()
    {
        Rows = Enum.GetValues<ActivityCategory>().Select(category => stored.FirstOrDefault(row => row.Category == category) is { } saved
                ? new CategoryBuffersRow { Category = category, PreparationMinutes = saved.PreparationMinutes, ChangingMinutes = saved.ChangingMinutes, Version = saved.Version }
                : new CategoryBuffersRow { Category = category, PreparationMinutes = CategoryBuffers.BuiltIn(category).Preparation, ChangingMinutes = CategoryBuffers.BuiltIn(category).Changing })
            .ToList(),
    };

    /// <summary>
    /// Every category once, in order, and each time from 0 to the most an activity allows, with the category named in
    /// the message so the summary at the top says which row. Run by the controller every time, so all are listed at once.
    /// </summary>
    public IEnumerable<ValidationResult> ProblemsAcrossTheForm()
    {
        if (!Rows.Select(row => row.Category).SequenceEqual(Enum.GetValues<ActivityCategory>()))
        {
            yield return new ValidationResult("Enter the times for every category.");
            yield break;
        }

        for (var i = 0; i < Rows.Count; i++)
        {
            var name = ActivityDisplay.CategoryName(Rows[i].Category);
            foreach (var (field, minutes, what) in new[]
                     {
                         (nameof(CategoryBuffersRow.PreparationMinutes), Rows[i].PreparationMinutes, "getting-ready"),
                         (nameof(CategoryBuffersRow.ChangingMinutes), Rows[i].ChangingMinutes, "changing"),
                     })
            {
                if (minutes is < 0 or > Activity.MaxBufferMinutes)
                {
                    yield return new ValidationResult($"Enter the {what} time for {name} as 0 to {Activity.MaxBufferMinutes} minutes.", [$"Rows[{i}].{field}"]);
                }
            }
        }
    }
}

/// <summary>One category's row on the form.</summary>
public class CategoryBuffersRow
{
    public ActivityCategory Category { get; set; }

    [Display(Name = "Getting ready (minutes)")]
    public int PreparationMinutes { get; set; }

    [Display(Name = "Changing afterwards (minutes)")]
    public int ChangingMinutes { get; set; }

    /// <summary>The version of the saved row the form was opened with; empty if it wasn't saved yet (requirements §9).</summary>
    public Guid Version { get; set; }
}
