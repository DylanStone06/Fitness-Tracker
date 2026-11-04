using System.Globalization;
using FitnessTracker.Domain.Workouts;
using FitnessTracker.Web.Data;
using NodaTime;

namespace FitnessTracker.Web.Models.Workouts;

/// <summary>A saved session's details as its page shows them: a heading ("Easy run", "Swim") and the figures given.</summary>
public sealed record SessionSummary(string Heading, IReadOnlyList<SessionFigure> Figures);

/// <summary>
/// FIT-3, FIT-4: the details a run and an other activity share: the time it took, its distance and how hard it felt
/// (Q29, Q35), written one way for both.
/// </summary>
public static class SessionDetails
{
    public const int LowestEffort = 1;

    public const int HighestEffort = 10;

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
    /// The figures <paramref name="log"/> has, in <paramref name="unit"/>: distance, time (with <paramref name="planned"/>
    /// under it), pace when <paramref name="withPace"/> (a run's: worked out here, never stored, Q25) and effort.
    /// </summary>
    public static IReadOnlyList<SessionFigure> Figures(SessionLog log, DistanceUnit unit, string planned, bool withPace)
    {
        var figures = new List<SessionFigure>();
        if (log.DistanceMetres is { } metres)
        {
            figures.Add(SessionFigure.WithUnit("Distance", Distances.Text(metres, unit)));
        }

        if (log.ActualSeconds is { } seconds)
        {
            var time = Duration.FromSeconds(seconds);
            figures.Add(new SessionFigure("Time", StopwatchText.Format(time), Note: $"planned {planned}"));
            if (withPace && log.DistanceMetres is { } distance && Pace.Text(time, distance, unit) is { } pace)
            {
                figures.Add(SessionFigure.WithUnit("Pace", pace));
            }
        }

        if (log.Effort is { } effort)
        {
            figures.Add(new SessionFigure("Effort", effort.ToString(CultureInfo.InvariantCulture), "of 10", EffortWord(effort)));
        }

        return figures;
    }
}
