using System.ComponentModel.DataAnnotations;
using FitnessTracker.Web.Data;

namespace FitnessTracker.Web.Models.Routines;

/// <summary>CAL-9: the form that duplicates a routine: only the new one's name; everything else is copied.</summary>
public class DuplicateRoutineViewModel
{
    // Trimmed as it is bound, so the checks see exactly what will be saved; the same rules as a routine's own form.
    [Required(ErrorMessage = "Enter a name for the routine.")]
    [StringLength(Routine.NameMaxLength, ErrorMessage = "The name must be {1} characters or fewer.")]
    [Display(Name = "Name of the copy")]
    public string Name { get; set => field = value?.Trim() ?? ""; } = "";

    /// <summary>Filled by the controller, never posted: the routine being copied.</summary>
    public Routine Original { get; set; } = new();

    /// <summary>Filled by the controller, never posted: how many of its activities are copied.</summary>
    public int Activities { get; set; }

    /// <summary>"Usual week copy", shortened to fit the name's length (never splitting an emoji's two halves).</summary>
    public static string NameFor(Routine original)
    {
        const string copy = " copy";
        if (original.Name.Length + copy.Length <= Routine.NameMaxLength)
        {
            return original.Name + copy;
        }

        var cut = Routine.NameMaxLength - copy.Length;
        if (char.IsHighSurrogate(original.Name[cut - 1]))
        {
            cut--;
        }

        return original.Name[..cut].TrimEnd() + copy;
    }
}
