using System.Globalization;
using FitnessTracker.Domain.Workouts;

namespace FitnessTracker.Web.Models.Workouts;

/// <summary>One exercise of a saved workout as its page shows it: its name and each set ("5 × 100 kg, rest 90 s").</summary>
public sealed record ExerciseLine(string Name, IReadOnlyList<string> Sets);

/// <summary>FIT-2: a saved workout as its page shows it: figure tiles, then each exercise with its sets.</summary>
public sealed record StrengthSummary(IReadOnlyList<SessionFigure> Figures, IReadOnlyList<ExerciseLine> Exercises);

/// <summary>FIT-2, Q31-Q33: how a gym session's sets are written on its page.</summary>
public static class StrengthDisplay
{
    /// <summary>
    /// The saved <paramref name="sets"/> by exercise, in <paramref name="unit"/>: tiles for the exercises, the sets and
    /// (when any set had a weight) the weight lifted; null with no sets.
    /// </summary>
    public static StrengthSummary? Summary(IReadOnlyList<WorkoutSet> sets, WeightUnit unit)
    {
        if (sets.Count == 0)
        {
            return null;
        }

        var workout = Workout.Summarise(sets);
        var figures = new List<SessionFigure>
        {
            new("Exercises", workout.Exercises.Count.ToString(CultureInfo.InvariantCulture)),
            new("Sets", workout.SetCount.ToString(CultureInfo.InvariantCulture)),
        };
        if (workout.LiftedGrams > 0)
        {
            figures.Add(SessionFigure.WithUnit("Weight lifted", Weights.Text(workout.LiftedGrams, unit)));
        }

        var exercises = workout.Exercises
            .Select(exercise => new ExerciseLine(exercise.Exercise, exercise.Sets.Select(set => SetText(set, unit)).ToList()))
            .ToList();
        return new StrengthSummary(figures, exercises);
    }

    /// <summary>One set: "5 × 100 kg", or "8 reps" with no weight (bodyweight), then ", rest 90 s" when given.</summary>
    public static string SetText(WorkoutSet set, WeightUnit unit)
    {
        var lifted = set.Grams is { } grams
            ? string.Create(CultureInfo.InvariantCulture, $"{set.Reps} × {Weights.Text(grams, unit)}")
            : string.Create(CultureInfo.InvariantCulture, $"{set.Reps} {(set.Reps == 1 ? "rep" : "reps")}");
        return set.RestSeconds is { } rest ? $"{lifted}, rest {RestText(rest)}" : lifted;
    }

    /// <summary>Rest as people say it: in seconds under 2 minutes ("45 s", "90 s"), then in minutes ("2 min", "2 min 30 s").</summary>
    public static string RestText(int seconds) => seconds < 120
        ? string.Create(CultureInfo.InvariantCulture, $"{seconds} s")
        : (seconds / 60, seconds % 60) switch
        {
            (var minutes, 0) => string.Create(CultureInfo.InvariantCulture, $"{minutes} min"),
            (var minutes, var andSeconds) => string.Create(CultureInfo.InvariantCulture, $"{minutes} min {andSeconds} s"),
        };
}
