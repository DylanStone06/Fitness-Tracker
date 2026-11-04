using System.Text.RegularExpressions;

namespace FitnessTracker.Domain.Workouts;

/// <summary>
/// FIT-2, Q4: the exercises suggested before the user has any of their own: common lifts and bodyweight exercises. A
/// list in code, not stored, so it can grow without a migration; an exercise is stored for a user the first time they
/// use it (Q30).
/// </summary>
public static class StarterExercises
{
    public static readonly IReadOnlyList<string> Names =
    [
        "Back squat", "Front squat", "Deadlift", "Romanian deadlift", "Bench press", "Incline bench press",
        "Overhead press", "Barbell row", "Pull-up", "Chin-up", "Lat pulldown", "Seated cable row",
        "Dumbbell bench press", "Dumbbell shoulder press", "Dumbbell row", "Lunge", "Bulgarian split squat", "Leg press",
        "Leg curl", "Leg extension", "Calf raise", "Hip thrust", "Bicep curl", "Tricep pushdown",
        "Dip", "Push-up", "Plank", "Hanging leg raise", "Kettlebell swing", "Face pull",
    ];
}

/// <summary>Q30: an exercise is known by its name as typed, tidied, and matched ignoring capitals.</summary>
public static partial class ExerciseNames
{
    public const int MaxLength = 60;

    /// <summary>The name as it is kept: spaces at either end removed, and any run of spaces (or tabs) inside made one.</summary>
    public static string Tidy(string name) => Spaces().Replace(name.Trim(), " ");

    /// <summary>
    /// What two names are compared by: tidied and in capitals, so "back  SQUAT" is Back squat. Upper-cased here, not by
    /// the database, whose own ignoring of case only covers A to Z ("Ć" and "ć" are one letter too).
    /// </summary>
    public static string Key(string name) => Tidy(name).ToUpperInvariant();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}

/// <summary>FIT-2: one set: an exercise, its reps, its weight in whole grams (Q33; none for bodyweight) and rest after it.</summary>
public sealed record WorkoutSet(string Exercise, int Reps, int? Grams, int? RestSeconds);

/// <summary>One exercise's sets, in the order they were done.</summary>
public sealed record ExerciseSets(string Exercise, IReadOnlyList<WorkoutSet> Sets);

/// <summary>A gym session's sets by exercise, how many sets in all, and the weight lifted (reps times weight, in grams).</summary>
public sealed record WorkoutSummary(IReadOnlyList<ExerciseSets> Exercises, int SetCount, long LiftedGrams);

public static class Workout
{
    /// <summary>
    /// <paramref name="sets"/> grouped by exercise in the order each was first used (names matched as
    /// <see cref="ExerciseNames.Key"/>, the first spelling kept), with the total sets and the weight lifted: reps times
    /// weight, summed, a set with no weight counting nothing. In grams as a long: 60 sets of 1,000 reps at 1,000 kg is
    /// more than an int holds.
    /// </summary>
    public static WorkoutSummary Summarise(IReadOnlyList<WorkoutSet> sets)
    {
        var exercises = sets
            .GroupBy(set => ExerciseNames.Key(set.Exercise), StringComparer.Ordinal)
            .Select(group => new ExerciseSets(group.First().Exercise, group.ToList()))
            .ToList();
        var lifted = sets.Sum(set => (long)set.Reps * (set.Grams ?? 0));
        return new WorkoutSummary(exercises, sets.Count, lifted);
    }
}
