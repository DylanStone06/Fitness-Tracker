using System.ComponentModel.DataAnnotations;

namespace FitnessTracker.Web.Models.Activities;

/// <summary>
/// A form that saves through the check on save (SCH-1) and can keep clashes with "Save anyway" (§7.2, Q2): only the
/// clashes the page showed, identified by <see cref="ShownClashes"/>. The clash box (_ClashBox) is shared by them.
/// </summary>
public interface IConfirmsClashes
{
    [Display(Name = "Save anyway, with these clashes")]
    bool ConfirmClashes { get; set; }

    string? ShownClashes { get; set; }
}
