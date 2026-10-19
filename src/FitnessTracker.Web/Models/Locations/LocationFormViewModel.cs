using System.ComponentModel.DataAnnotations;
using FitnessTracker.Web.Data;

namespace FitnessTracker.Web.Models.Locations;

/// <summary>The add and edit forms for a location. Name only until Phase 4 adds opening hours.</summary>
public class LocationFormViewModel
{
    // Trimmed as it is bound, so the checks below see exactly what will be saved
    // (phone keyboards add a space after an autocompleted word).
    [Required(ErrorMessage = "Enter a name for the location.")]
    [StringLength(Location.NameMaxLength, ErrorMessage = "The name must be {1} characters or fewer.")]
    public string Name { get; set => field = value?.Trim() ?? ""; } = "";

    /// <summary>Edit only: the version of the location the form was opened with (requirements §9).</summary>
    public Guid Version { get; set; }
}
