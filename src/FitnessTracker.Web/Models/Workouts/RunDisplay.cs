using FitnessTracker.Domain.Workouts;
using FitnessTracker.Web.Data;

namespace FitnessTracker.Web.Models.Workouts;

/// <summary>FIT-3, Q26-Q29: how a run's details are written, in the form's lists and on its page.</summary>
public static class RunDisplay
{
    /// <summary>The type as its list offers it: "Easy", …, "Custom".</summary>
    public static string TypeName(RunType type) => type switch
    {
        RunType.Easy => "Easy",
        RunType.Long => "Long",
        RunType.Tempo => "Tempo",
        RunType.Intervals => "Intervals",
        RunType.Recovery => "Recovery",
        _ => "Custom",
    };

    /// <summary>A saved run's heading: "Easy run", "Intervals", a custom type's own name, or "Your run" with no type.</summary>
    public static string Heading(RunType? type, string? customName) => type switch
    {
        null => "Your run",
        RunType.Custom => customName ?? "Custom run",
        RunType.Intervals => "Intervals",
        { } given => $"{TypeName(given)} run",
    };

    /// <summary>
    /// The run <paramref name="log"/> saved, in <paramref name="unit"/>, with <paramref name="planned"/> ("1 h") beside its
    /// time; null when it has no details. Pace is worked out, never stored (Q25).
    /// </summary>
    public static SessionSummary? Summary(SessionLog log, DistanceUnit unit, string planned)
    {
        var figures = SessionDetails.Figures(log, unit, planned, withPace: true);
        return figures.Count == 0 && log.RunType is null ? null : new SessionSummary(Heading(log.RunType, log.CustomRunType), figures);
    }
}
