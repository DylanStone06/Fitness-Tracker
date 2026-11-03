using System.Globalization;
using FitnessTracker.Domain.Workouts;
using FitnessTracker.Web.Data;
using NodaTime;

namespace FitnessTracker.Web.Models.Workouts;

/// <summary>One of a run's figures as its page shows it: "Distance", "5.2" "km"; "Effort", "6" "of 10", "moderate".</summary>
public sealed record RunFigure(string Label, string Value, string? Unit = null, string? Note = null);

/// <summary>FIT-3: a saved run as its page shows it: a heading ("Easy run") and the figures that were given.</summary>
public sealed record RunSummary(string Heading, IReadOnlyList<RunFigure> Figures);

/// <summary>FIT-3, Q26-Q29: how a run's details are written, in the form's lists and on its page.</summary>
public static class RunDisplay
{
    public const int LowestEffort = 1;

    public const int HighestEffort = 10;

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

    /// <summary>Q29: what an effort of 1 to 10 means, on a common perceived-effort scale.</summary>
    public static string EffortWord(int effort) => effort switch
    {
        1 => "very easy",
        2 or 3 => "easy",
        4 or 5 or 6 => "moderate",
        7 or 8 => "hard",
        9 => "very hard",
        10 => "all out",
        _ => throw new ArgumentOutOfRangeException(nameof(effort), effort, "Effort is from 1 to 10."),
    };

    /// <summary>An effort as its list offers it: "6: moderate".</summary>
    public static string EffortChoice(int effort) => string.Create(CultureInfo.InvariantCulture, $"{effort}: {EffortWord(effort)}");

    /// <summary>
    /// The run <paramref name="log"/> saved, in <paramref name="unit"/>, with <paramref name="planned"/> ("1 h") beside its
    /// time; null when it has no details. Pace is worked out here, never stored (Q25).
    /// </summary>
    public static RunSummary? Summary(SessionLog log, DistanceUnit unit, string planned)
    {
        var figures = new List<RunFigure>();
        if (log.DistanceMetres is { } metres)
        {
            figures.Add(Split("Distance", Distances.Text(metres, unit)));
        }

        if (log.ActualSeconds is { } seconds)
        {
            var time = Duration.FromSeconds(seconds);
            figures.Add(new RunFigure("Time", StopwatchText.Format(time), Note: $"planned {planned}"));
            if (log.DistanceMetres is { } distance && Pace.Text(time, distance, unit) is { } pace)
            {
                figures.Add(Split("Pace", pace));
            }
        }

        if (log.Effort is { } effort)
        {
            figures.Add(new RunFigure("Effort", effort.ToString(CultureInfo.InvariantCulture), "of 10", EffortWord(effort)));
        }

        return figures.Count == 0 && log.RunType is null ? null : new RunSummary(Heading(log.RunType, log.CustomRunType), figures);
    }

    // "5.2 km" as the figure 5.2 and its unit, which is shown smaller. The domain writes a number, a space and the unit,
    // and a number never has a space in it.
    private static RunFigure Split(string label, string text)
    {
        var space = text.IndexOf(' ', StringComparison.Ordinal);
        return new RunFigure(label, text[..space], text[(space + 1)..]);
    }
}
