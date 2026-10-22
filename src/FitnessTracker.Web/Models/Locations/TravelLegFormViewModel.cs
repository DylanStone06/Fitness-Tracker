using System.ComponentModel.DataAnnotations;
using FitnessTracker.Web.Data;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace FitnessTracker.Web.Models.Locations;

/// <summary>The add and edit forms for a travel leg (TRV-2).</summary>
public class TravelLegFormViewModel
{
    [Display(Name = "From")]
    [Required(ErrorMessage = "Choose where the journey starts.")]
    public Guid? FromLocationId { get; set; }

    [Display(Name = "To")]
    [Required(ErrorMessage = "Choose where the journey ends.")]
    public Guid? ToLocationId { get; set; }

    [Display(Name = "Journey time (minutes)")]
    [Range(1, TravelLeg.MaxMinutes, ErrorMessage = "Enter between {1} and {2} minutes.")]
    public int Minutes { get; set; } = 15;

    [Display(Name = "How you travel")]
    public TravelMode Mode { get; set; }

    /// <summary>Add only: also add the journey back with the same time, unless it already has one.</summary>
    [Display(Name = "Also add the journey back, taking the same time")]
    public bool AlsoAddReturnJourney { get; set; } = true;

    /// <summary>Edit only: the version of the leg the form was opened with (requirements §9).</summary>
    public Guid Version { get; set; }

    /// <summary>The signed-in person's locations, for the lists. Filled by the controller, never posted.</summary>
    [BindNever]
    [ValidateNever]
    public IReadOnlyList<Location> Locations { get; set; } = [];

    public static TravelLegFormViewModel From(TravelLeg leg) => new()
    {
        FromLocationId = leg.FromLocationId,
        ToLocationId = leg.ToLocationId,
        Minutes = leg.Minutes,
        Mode = leg.Mode,
        AlsoAddReturnJourney = false,
        Version = leg.Version,
    };
}
