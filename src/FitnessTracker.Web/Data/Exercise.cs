namespace FitnessTracker.Web.Data;

/// <summary>
/// FIT-2, Q30: one of the user's exercises, stored the first time they save a set with that name, from the starter list
/// (Q4) or their own. Matched by <see cref="NameKey"/>, so "back squat" and "Back Squat" are one exercise.
/// </summary>
public class Exercise : IOwnedByUser, IVersioned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string OwnerId { get; set; } = "";

    public Guid Version { get; set; }

    /// <summary>As first typed, tidied (ExerciseNames.Tidy): up to ExerciseNames.MaxLength characters.</summary>
    public string Name { get; set; } = "";

    /// <summary>ExerciseNames.Key of the name: unique per user.</summary>
    public string NameKey { get; set; } = "";
}

/// <summary>
/// FIT-2: one set of a gym session, in its log: an exercise, reps, weight in whole grams (Q33; none for bodyweight) and
/// rest after it in seconds (Q32), at its place in the session. A session's sets are saved together, under its log's
/// version (requirements §9).
/// </summary>
public class SessionSet : IOwnedByUser, IVersioned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string OwnerId { get; set; } = "";

    public Guid Version { get; set; }

    public Guid SessionLogId { get; set; }

    /// <summary>Its place in the session, from 0.</summary>
    public int Position { get; set; }

    public Guid ExerciseId { get; set; }

    public int Reps { get; set; }

    public int? WeightGrams { get; set; }

    public int? RestSeconds { get; set; }
}
