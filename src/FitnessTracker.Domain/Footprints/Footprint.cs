using NodaTime;

namespace FitnessTracker.Domain.Footprints;

/// <summary>The parts of a footprint, in the order they happen (requirements Â§7.1).</summary>
public enum FootprintPart
{
    Preparation,
    TravelOut,
    Activity,
    Changing,
    TravelBack,
}

/// <summary>Why a footprint can't be made (H8).</summary>
public enum FootprintProblem
{
    ActivityNotLongerThanZero,
    NegativePreparation,
    NegativeChanging,
    NegativeTravelOut,
    NegativeTravelBack,
    LongerThan24Hours,
}

/// <summary>One part of a footprint and the exact time it takes up, half-open [start, end).</summary>
public readonly record struct FootprintSegment(FootprintPart Part, Interval Time);

/// <summary>Time before an activity to get ready, and after it to change or wind down (glossary: buffer).</summary>
public readonly record struct Buffers(Duration Preparation, Duration Changing)
{
    public static Buffers None => new(Duration.Zero, Duration.Zero);
}

/// <summary>
/// The journeys to and from an activity away from base. Each direction has its own length (TRV-2: home to gym may
/// differ from gym to home). An activity at base has <see cref="None"/>.
/// </summary>
public readonly record struct Journeys(Duration Out, Duration Back)
{
    public static Journeys None => new(Duration.Zero, Duration.Zero);
}

/// <summary>
/// Requirements Â§7.1: the whole block of time an activity reserves, which is what conflicts are checked on.
/// footprint = preparation + travel out + activity + changing + travel back. Every length is elapsed time, so a
/// footprint is the same length on the nights the clocks change (Â§8).
/// </summary>
public sealed class Footprint
{
    // H8. Elapsed time, not "a day": the days the clocks change last 23 or 25 hours.
    private static readonly Duration Longest = Duration.FromHours(24);

    private Footprint(Interval activity, Interval whole, IReadOnlyList<FootprintSegment> segments)
    {
        Activity = activity;
        Whole = whole;
        Segments = segments;
    }

    /// <summary>The activity itself, without buffers or travel: the part opening hours apply to (H4).</summary>
    public Interval Activity { get; }

    /// <summary>From the start of preparation to the end of the journey back.</summary>
    public Interval Whole { get; }

    /// <summary>The parts in time order. Parts of zero length are left out.</summary>
    public IReadOnlyList<FootprintSegment> Segments { get; }

    /// <summary>The footprint around an activity. Refused, with the reasons, if <see cref="ProblemsWith"/> finds any.</summary>
    public static Footprint Around(Interval activity, Buffers buffers, Journeys journeys)
    {
        if (!activity.HasStart || !activity.HasEnd)
        {
            throw new ArgumentException("An activity needs both a start and an end.", nameof(activity));
        }

        var problems = ProblemsWith(activity.Duration, buffers, journeys);
        if (problems.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", problems.Select(Describe)), ArgumentNameFor(problems[0]));
        }

        var travelOutStart = activity.Start - journeys.Out;
        var preparationStart = travelOutStart - buffers.Preparation;
        var changingEnd = activity.End + buffers.Changing;
        var travelBackEnd = changingEnd + journeys.Back;

        FootprintSegment[] parts =
        [
            new(FootprintPart.Preparation, new Interval(preparationStart, travelOutStart)),
            new(FootprintPart.TravelOut, new Interval(travelOutStart, activity.Start)),
            new(FootprintPart.Activity, activity),
            new(FootprintPart.Changing, new Interval(activity.End, changingEnd)),
            new(FootprintPart.TravelBack, new Interval(changingEnd, travelBackEnd)),
        ];

        return new Footprint(
            activity,
            new Interval(preparationStart, travelBackEnd),
            parts.Where(part => part.Time.Duration > Duration.Zero).ToList());
    }

    /// <summary>
    /// H8: the activity lasts longer than zero, buffers and journeys aren't negative (zero is fine), and the whole
    /// footprint is at most 24 hours. Every broken limit is reported, in the order of the enum, each naming its own
    /// part. A negative part counts as zero towards the 24 hours, so it can't hide a footprint that is too long anyway.
    /// </summary>
    public static IReadOnlyList<FootprintProblem> ProblemsWith(Duration activityLength, Buffers buffers, Journeys journeys)
    {
        (Duration Length, FootprintProblem Problem)[] parts =
        [
            (buffers.Preparation, FootprintProblem.NegativePreparation),
            (buffers.Changing, FootprintProblem.NegativeChanging),
            (journeys.Out, FootprintProblem.NegativeTravelOut),
            (journeys.Back, FootprintProblem.NegativeTravelBack),
        ];

        var problems = new List<FootprintProblem>();
        if (activityLength <= Duration.Zero)
        {
            problems.Add(FootprintProblem.ActivityNotLongerThanZero);
        }

        problems.AddRange(parts.Where(part => part.Length < Duration.Zero).Select(part => part.Problem));

        var whole = parts.Aggregate(NotNegative(activityLength), (total, part) => total + NotNegative(part.Length));
        if (whole > Longest)
        {
            problems.Add(FootprintProblem.LongerThan24Hours);
        }

        return problems;
    }

    /// <summary>A plain-English reason, for forms and explanations.</summary>
    public static string Describe(FootprintProblem problem) => problem switch
    {
        FootprintProblem.ActivityNotLongerThanZero => "The activity must last longer than zero minutes.",
        FootprintProblem.NegativePreparation => "Preparation time can't be less than zero.",
        FootprintProblem.NegativeChanging => "Changing time can't be less than zero.",
        FootprintProblem.NegativeTravelOut => "Travel time there can't be less than zero.",
        FootprintProblem.NegativeTravelBack => "Travel time back can't be less than zero.",
        FootprintProblem.LongerThan24Hours => "With preparation, travel and changing, it would take up more than 24 hours.",
        _ => throw new ArgumentOutOfRangeException(nameof(problem), problem, null),
    };

    private static Duration NotNegative(Duration length) => length < Duration.Zero ? Duration.Zero : length;

    // The argument of Around that a problem is about, for ArgumentException.ParamName.
    private static string ArgumentNameFor(FootprintProblem problem) => problem switch
    {
        FootprintProblem.NegativePreparation or FootprintProblem.NegativeChanging => "buffers",
        FootprintProblem.NegativeTravelOut or FootprintProblem.NegativeTravelBack => "journeys",
        _ => "activity",
    };
}
