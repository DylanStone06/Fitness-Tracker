using FitnessTracker.Domain.Workouts;

namespace FitnessTracker.Domain.Tests.Workouts;

// FIT-2: a gym session's sets. Q4: about 30 starter exercises; Q30: an exercise is known by its name, ignoring capitals
// and extra spaces; Q33: weights in whole grams.
public class WorkoutTests
{
    [Fact]
    public void TheStarterList_HasThirtyExercises_EachOnceIgnoringCapitals_AndShortEnough()
    {
        Assert.Equal(30, StarterExercises.Names.Count);
        Assert.Equal(30, StarterExercises.Names.Select(ExerciseNames.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.All(StarterExercises.Names, name => Assert.InRange(name.Length, 1, ExerciseNames.MaxLength));
        Assert.All(StarterExercises.Names, name => Assert.Equal(name, ExerciseNames.Tidy(name)));
        Assert.Contains("Back squat", StarterExercises.Names);
        Assert.Contains("Romanian deadlift", StarterExercises.Names);
    }

    // Q30: as typed, tidied: spaces at either end gone, and runs of spaces inside made one.
    [Theory]
    [InlineData("  Back   squat ", "Back squat")]
    [InlineData("Push-up", "Push-up")]
    [InlineData("Bench\tpress", "Bench press")]
    public void ANameAsTyped_IsTidied(string typed, string tidied)
    {
        Assert.Equal(tidied, ExerciseNames.Tidy(typed));
    }

    // ... and matched ignoring capitals, letters outside English too (SQLite's own ignoring of case only covers A-Z).
    [Theory]
    [InlineData("Back squat", "back SQUAT")]
    [InlineData("  Back squat", "Back  squat")]
    [InlineData("Ćwiczenie", "ćwiczenie")]
    public void TheSameExercise_HasTheSameKey(string one, string other)
    {
        Assert.Equal(ExerciseNames.Key(one), ExerciseNames.Key(other));
    }

    [Fact]
    public void DifferentExercises_HaveDifferentKeys()
    {
        Assert.NotEqual(ExerciseNames.Key("Back squat"), ExerciseNames.Key("Front squat"));
    }

    // Sets are grouped by exercise in the order each was first used, ignoring capitals and keeping the first spelling;
    // the weight lifted is reps times weight, summed, a set with no weight (bodyweight) counting nothing.
    [Fact]
    public void AWorkout_IsGroupedByExercise_WithItsSetsAndTheWeightLifted()
    {
        WorkoutSet[] sets =
        [
            new("Back squat", 5, 100_000, 120),
            new("Pull-up", 8, null, null),
            new("back squat", 5, 102_500, null),
            new("Pull-up", 6, null, 90),
        ];

        var summary = Workout.Summarise(sets);

        Assert.Equal(["Back squat", "Pull-up"], summary.Exercises.Select(exercise => exercise.Exercise));
        Assert.Equal([5, 5], summary.Exercises[0].Sets.Select(set => set.Reps));
        Assert.Equal([8, 6], summary.Exercises[1].Sets.Select(set => set.Reps));
        Assert.Equal(4, summary.SetCount);
        Assert.Equal((5L * 100_000) + (5L * 102_500), summary.LiftedGrams);
    }

    // The most a session takes (60 sets of 1,000 reps at 1,000 kg) is more grams than an int holds: still counted.
    [Fact]
    public void TheWeightLifted_CanBeMoreGramsThanAnIntHolds()
    {
        var sets = Enumerable.Repeat(new WorkoutSet("Leg press", 1_000, 1_000_000, null), 60).ToList();

        Assert.Equal(60_000_000_000L, Workout.Summarise(sets).LiftedGrams);
    }

    [Fact]
    public void NoSets_IsAnEmptyWorkout()
    {
        var summary = Workout.Summarise([]);

        Assert.Equal((0, 0, 0L), (summary.Exercises.Count, summary.SetCount, summary.LiftedGrams));
    }
}
