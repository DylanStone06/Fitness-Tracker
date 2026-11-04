using FitnessTracker.Domain.Workouts;
using FitnessTracker.Web.Data;

namespace FitnessTracker.Web.Models.Workouts;

/// <summary>FIT-4, Q34-Q36: how an other activity's details are written, in the form's list and on its page.</summary>
public static class OtherDisplay
{
    /// <summary>The type as its list offers it, and as a saved one's heading: "Walk", …, "Custom".</summary>
    public static string TypeName(OtherType type) => type switch
    {
        OtherType.Walk => "Walk",
        OtherType.Cycle => "Cycle",
        OtherType.Swim => "Swim",
        OtherType.Mobility => "Mobility",
        _ => "Custom",
    };

    /// <summary>A saved one's heading: its type, a custom type's own name, or "Your session" with no type.</summary>
    public static string Heading(OtherType? type, string? customName) => type switch
    {
        null => "Your session",
        OtherType.Custom => customName ?? "Custom",
        { } given => TypeName(given),
    };

    /// <summary>
    /// The activity <paramref name="log"/> saved, in <paramref name="unit"/>, with <paramref name="planned"/> beside its
    /// time; null when it has no details. No pace (Q36: speed and swim pace come with FIT-7).
    /// </summary>
    public static SessionSummary? Summary(SessionLog log, DistanceUnit unit, string planned)
    {
        var figures = SessionDetails.Figures(log, unit, planned, withPace: false);
        return figures.Count == 0 && log.OtherType is null ? null : new SessionSummary(Heading(log.OtherType, log.CustomOtherType), figures);
    }
}
