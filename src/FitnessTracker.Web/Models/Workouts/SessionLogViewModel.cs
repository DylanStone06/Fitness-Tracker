using System.ComponentModel.DataAnnotations;
using System.Globalization;
using FitnessTracker.Domain.Recurrence;
using FitnessTracker.Domain.Scheduling;
using FitnessTracker.Domain.Time;
using FitnessTracker.Domain.Workouts;
using FitnessTracker.Web.Data;
using FitnessTracker.Web.Models.Activities;
using NodaTime;
using NodaTime.Text;

namespace FitnessTracker.Web.Models.Workouts;

/// <summary>FIT-1, Q21: what a session can be marked as on its page: back to planned, done or skipped.</summary>
public enum SessionChoice
{
    Planned,
    Done,
    Skipped,
}

/// <summary>FIT-2, Q31: one row of a gym session's sets, as typed; a row left blank is ignored.</summary>
public class SetRow
{
    /// <summary>Tidied as it is bound (ExerciseNames.Tidy), so the checks see the name that will be kept.</summary>
    [Display(Name = "Exercise")]
    public string? Exercise { get; set => field = string.IsNullOrWhiteSpace(value) ? null : ExerciseNames.Tidy(value); }

    [Display(Name = "Reps")]
    public int? Reps { get; set; }

    /// <summary>As typed, in the user's weight unit ("102.5").</summary>
    [Display(Name = "Weight")]
    public string? Weight { get; set; }

    /// <summary>Rest after the set, in seconds.</summary>
    [Display(Name = "Rest")]
    public int? Rest { get; set; }

    public bool IsBlank() => Exercise is null && Reps is null && string.IsNullOrWhiteSpace(Weight) && Rest is null;
}

/// <summary>
/// FIT-1: one session's page: when it is, its status (Q22: where a rescheduled one was), "Not logged" once its time has
/// passed unmarked (T20), and the choices for marking it. A run's, an other activity's and a gym session's page also log
/// what it was like (FIT-3, FIT-4, FIT-2), and show it once saved. Only <see cref="Date"/>, <see cref="Version"/>, the
/// boxes' units, <see cref="Mark"/>, the notes and the boxes of the session's kind are posted.
/// </summary>
public class SessionLogViewModel
{
    /// <summary>The most a run's distance box takes, in km or miles: far more than anyone runs in a session.</summary>
    public const int MaxDistance = 1000;

    /// <summary>
    /// The time, distance and effort boxes runs and other activities share (FIT-3, FIT-4): only saving one of those as
    /// done reads them; the other buttons, and other kinds of session, ignore what is in them, even what can't be read.
    /// </summary>
    public static readonly IReadOnlyList<string> DetailBoxes =
        [nameof(TimeHours), nameof(TimeMinutes), nameof(TimeSeconds), nameof(Distance), nameof(Effort)];

    /// <summary>A run's own boxes, read only when a run is saved as done.</summary>
    public static readonly IReadOnlyList<string> RunTypeBoxes = [nameof(RunType), nameof(CustomRunType)];

    /// <summary>An other activity's own boxes, read only when one is saved as done (FIT-4).</summary>
    public static readonly IReadOnlyList<string> OtherTypeBoxes = [nameof(OtherType), nameof(CustomOtherType)];

    /// <summary>Q31: the most sets one session can have.</summary>
    public const int MaxSets = 60;

    /// <summary>Q31: rows shown to start with, and added by "Add more rows".</summary>
    public const int RowsAtATime = 6;

    private const decimal MinDistance = 0.01m;

    private const int MaxHours = 24;

    private const int MaxReps = 1000;

    private const int MaxRestSeconds = 3600;

    private static readonly LocalDatePattern DayPattern = LocalDatePattern.CreateWithInvariantCulture("ddd d MMM");

    /// <summary>The date the activity's rule puts the session on, which knows it (CAL-4).</summary>
    public string Date { get; set; } = "";

    /// <summary>
    /// Requirements §9: the version of the session's log the page was opened with (empty when it had none), so a save
    /// from a page opened before another tab changed the session is refused.
    /// </summary>
    public Guid Version { get; set; }

    /// <summary>
    /// Q27, Q33, requirements §9: the unit the page's distance boxes were filled in and labelled with, posted back so
    /// what was typed is read in it, even when Preferences changed in another tab after the page was opened.
    /// </summary>
    public DistanceUnit? BoxDistanceUnit { get; set; }

    /// <summary>As <see cref="BoxDistanceUnit"/>, for the weight boxes of a gym session's sets.</summary>
    public WeightUnit? BoxWeightUnit { get; set; }

    public SessionChoice Mark { get; set; }

    // FIT-3, Q26: a run's details, each optional, in the order the form asks for them.

    [Display(Name = "Type of run")]
    public RunType? RunType { get; set; }

    /// <summary>Only with <see cref="Data.RunType.Custom"/>; its box is hidden otherwise.</summary>
    [Display(Name = "Name of the custom type")]
    public string? CustomRunType { get; set => field = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); }

    // FIT-4, Q34: an other activity's type; its time, distance and effort are the boxes below, shared with runs.

    [Display(Name = "Type of activity")]
    public OtherType? OtherType { get; set; }

    /// <summary>Only with <see cref="Data.OtherType.Custom"/>; its box is hidden otherwise.</summary>
    [Display(Name = "Name of the custom type")]
    public string? CustomOtherType { get; set => field = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); }

    [Display(Name = "Hours")]
    public int? TimeHours { get; set; }

    [Display(Name = "Minutes")]
    public int? TimeMinutes { get; set; }

    [Display(Name = "Seconds")]
    public int? TimeSeconds { get; set; }

    /// <summary>As typed, in <see cref="DistanceUnit"/> ("5.2").</summary>
    [Display(Name = "Distance")]
    public string? Distance { get; set; }

    [Display(Name = "How hard it felt")]
    public int? Effort { get; set; }

    // FIT-2, Q31: a gym session's sets, in the order the form asks for them.

    public List<SetRow> Sets { get; set; } = [];

    /// <summary>How many set rows the page shows, posted back so "Add more rows" adds to it.</summary>
    public int Rows { get; set; } = RowsAtATime;

    /// <summary>"Add more rows" was pressed: the page again with more rows, nothing saved.</summary>
    public bool MoreRows { get; set; }

    /// <summary>FIT-2: notes on any session, saved with its mark (kept when skipped); trimmed, and blank is none.</summary>
    [Display(Name = "Notes")]
    [StringLength(SessionLog.NotesMaxLength, ErrorMessage = "Notes must be {1} characters or fewer.")]
    public string? Notes { get; set => field = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); }

    public Guid ActivityId { get; private set; }

    public string Title { get; private set; } = "";

    /// <summary>When it is now: "Thu 29 Oct, 18:00 – 19:00".</summary>
    public string When { get; private set; } = "";

    /// <summary>"Planned", "Done", "Skipped" or "Rescheduled from Tue 27 Oct, 07:00".</summary>
    public string Status { get; private set; } = "";

    public SessionMark? Marked { get; private set; }

    /// <summary>T20: its time has passed and it isn't marked; shown, never a completion.</summary>
    public bool NotLogged { get; private set; }

    /// <summary>Q21: once it has started.</summary>
    public bool CanMarkDone { get; private set; }

    /// <summary>FIT-3: a run's session, whose page logs what the run was like.</summary>
    public bool IsRun { get; private set; }

    /// <summary>
    /// Q25: what distances are typed and shown in, as chosen in Preferences; on a page shown again after a problem, the
    /// unit its boxes were typed in (<see cref="KeepTyped"/>).
    /// </summary>
    public DistanceUnit DistanceUnit { get; private set; }

    /// <summary>How long it is planned to take, written as everywhere else: "1 h".</summary>
    public string PlannedLength { get; private set; } = "";

    /// <summary>FIT-4: an Other exercise session's, whose page logs what it was.</summary>
    public bool IsOther { get; private set; }

    /// <summary>A run or an other activity marked done, as saved, when it has details.</summary>
    public SessionSummary? Details { get; private set; }

    /// <summary>FIT-2: a gym session's, whose page logs its sets.</summary>
    public bool IsGym { get; private set; }

    /// <summary>Q33 with Q25: what weights are typed and shown in, as <see cref="DistanceUnit"/> is for distances.</summary>
    public WeightUnit WeightUnit { get; private set; }

    /// <summary>A gym session marked done, as saved, when it has sets.</summary>
    public StrengthSummary? Workout { get; private set; }

    /// <summary>Q30: what the exercise boxes suggest: the user's own exercises, then the starter list's others.</summary>
    public IReadOnlyList<string> ExerciseSuggestions { get; private set; } = [];

    /// <summary>
    /// The page for <paramref name="occurrence"/> of <paramref name="session"/> (an activity in
    /// <paramref name="category"/>), with its <paramref name="log"/> (null when it isn't marked), at
    /// <paramref name="now"/>. A run marked done has its boxes filled with what was saved.
    /// </summary>
    public static SessionLogViewModel For(
        PlannedActivity session, ActivityCategory category, Occurrence occurrence, SessionLog? log, Instant now, ClockFormat clock, DistanceUnit unit)
    {
        var series = session.Series;
        var marked = log?.Mark;
        var state = Sessions.StateOf(series, occurrence.Date, marked, now)!;
        var item = new AgendaItem(session, occurrence, null, null, []);
        var model = new SessionLogViewModel
        {
            Date = FormInput.Date.Format(occurrence.Date),
            Version = log?.Version ?? Guid.Empty,
            ActivityId = session.Id,
            Title = session.Title,
            When = $"{DayPattern.Format(occurrence.Time.Start.InZone(series.Zone).Date)}, {Agenda.Starts(item, clock)} – {Agenda.Ends(item, clock)}",
            Status = state.Status switch
            {
                SessionStatus.Completed => "Done",
                SessionStatus.Skipped => "Skipped",
                SessionStatus.Rescheduled => $"Rescheduled from {WhereItWas(series, occurrence.Date, clock)}",
                _ => "Planned",
            },
            Marked = marked,
            NotLogged = state.NotLogged,
            CanMarkDone = Sessions.CanMarkDone(occurrence, now),
            IsRun = category == ActivityCategory.Run,
            IsGym = category == ActivityCategory.Gym,
            IsOther = category == ActivityCategory.OtherExercise,
            DistanceUnit = unit,
            BoxDistanceUnit = unit,
            PlannedLength = ActivityDisplay.Length((int)occurrence.Time.Duration.TotalMinutes),
            Notes = log?.Notes,
        };

        if (model.IsRun && log is { Mark: SessionMark.Done })
        {
            model.Details = RunDisplay.Summary(log, unit, model.PlannedLength);
            model.FillRunBoxes(log, unit);
        }
        else if (model.IsOther && log is { Mark: SessionMark.Done })
        {
            model.Details = OtherDisplay.Summary(log, unit, model.PlannedLength);
            model.OtherType = log.OtherType;
            model.CustomOtherType = log.CustomOtherType;
            model.FillDetailBoxes(log, unit);
        }

        return model;
    }

    /// <summary>
    /// FIT-3: problems with a run's details as typed, for saving it as done, listed with any the boxes' own reading
    /// found: its type, then the details it shares with other activities (<see cref="DetailProblems"/>).
    /// </summary>
    public IEnumerable<ValidationResult> RunProblems(DistanceUnit unit) =>
        CustomTypeProblems(RunType == Data.RunType.Custom, CustomRunType, nameof(CustomRunType)).Concat(DetailProblems(unit));

    /// <summary>FIT-4: problems with an other activity's details as typed, as for a run's (Q34-Q36).</summary>
    public IEnumerable<ValidationResult> OtherProblems(DistanceUnit unit) =>
        CustomTypeProblems(OtherType == Data.OtherType.Custom, CustomOtherType, nameof(CustomOtherType)).Concat(DetailProblems(unit));

    /// <summary>
    /// FIT-3, FIT-4: problems with the time, distance and effort boxes as typed. A blank time box counts as none of that
    /// unit, so "45" minutes alone is enough.
    /// </summary>
    public IEnumerable<ValidationResult> DetailProblems(DistanceUnit unit)
    {
        foreach (var problem in TimeProblems())
        {
            yield return problem;
        }

        if (!string.IsNullOrWhiteSpace(Distance))
        {
            var unitName = unit == DistanceUnit.Kilometres ? "km" : "miles";
            var amount = FormInput.ParseAmount(Distance);
            if (amount is null)
            {
                yield return new ValidationResult("Enter the distance as a number, for example 5.2.", [nameof(Distance)]);
            }
            else if (amount < MinDistance)
            {
                yield return new ValidationResult($"Enter a distance of at least 0.01 {unitName}.", [nameof(Distance)]);
            }
            else if (amount > MaxDistance)
            {
                yield return new ValidationResult($"Enter a distance of at most 1,000 {unitName}.", [nameof(Distance)]);
            }
        }

        if (Effort is < SessionDetails.LowestEffort or > SessionDetails.HighestEffort)
        {
            yield return new ValidationResult("Choose how hard it felt from the list.", [nameof(Effort)]);
        }
    }

    /// <summary>
    /// FIT-3: copies a run's details onto its <paramref name="log"/>, in seconds and whole metres (Q25). A name typed
    /// with a type other than custom isn't kept: its box is hidden then. Only call once <see cref="RunProblems"/> finds
    /// none.
    /// </summary>
    public void ApplyRunTo(SessionLog log, DistanceUnit unit)
    {
        log.RunType = RunType;
        log.CustomRunType = RunType == Data.RunType.Custom ? CustomRunType : null;
        ApplyDetailsTo(log, unit);
    }

    /// <summary>
    /// FIT-4: copies an other activity's details onto its <paramref name="log"/>, as a run's (a name typed with a type
    /// other than custom isn't kept). Only call once <see cref="OtherProblems"/> finds none.
    /// </summary>
    public void ApplyOtherTo(SessionLog log, DistanceUnit unit)
    {
        log.OtherType = OtherType;
        log.CustomOtherType = OtherType == Data.OtherType.Custom ? CustomOtherType : null;
        ApplyDetailsTo(log, unit);
    }

    /// <summary>
    /// FIT-2: a gym session's page: the workout as saved (<paramref name="saved"/>, none unless it is done) in
    /// <paramref name="unit"/>, its rows filled with it (weights as text that saves back to the same grams: Q33), at
    /// least one empty row after them, and the exercise names to suggest (Q30).
    /// </summary>
    public void FillWorkout(IReadOnlyList<WorkoutSet> saved, WeightUnit unit, IReadOnlyList<string> suggestions)
    {
        WeightUnit = unit;
        BoxWeightUnit = unit;
        ExerciseSuggestions = suggestions;
        Workout = StrengthDisplay.Summary(saved, unit);
        Sets = saved.Select(set => new SetRow
        {
            Exercise = set.Exercise,
            Reps = set.Reps,
            Weight = set.Grams is { } grams ? Weights.InputText(grams, unit) : null,
            Rest = set.RestSeconds,
        }).ToList();
        ShowRows((saved.Count / RowsAtATime * RowsAtATime) + RowsAtATime);
    }

    /// <summary>
    /// The page shown again after a problem, whose boxes still hold what was <paramref name="posted"/> (a box shows what
    /// was posted in it over the page's own value). They keep the units it was typed in, for their labels and for the
    /// next save (<see cref="BoxDistanceUnit"/>). A gym session's rows are the ones posted, every one, however many "Add
    /// more rows" had shown (Q31), and no more: a row left off would be dropped by the next save without a word, and a
    /// set saved since in another tab, shown after them, would be saved again unseen (requirements §9). A page posted
    /// with no rows (opened before the session started) shows the sets as saved now.
    /// </summary>
    public void KeepTyped(SessionLogViewModel posted)
    {
        if (posted.BoxDistanceUnit is { } distance)
        {
            DistanceUnit = distance;
            BoxDistanceUnit = distance;
        }

        if (posted.BoxWeightUnit is { } weight)
        {
            WeightUnit = weight;
            BoxWeightUnit = weight;
        }

        if (IsGym)
        {
            if (posted.Sets.Count > 0)
            {
                Sets = posted.Sets;
            }

            ShowRows(Math.Max(posted.Rows, Sets.Count));
        }
    }

    /// <summary>Q31: shows <paramref name="rows"/> set rows (at most <see cref="MaxSets"/>), the ones added empty.</summary>
    public void ShowRows(int rows)
    {
        Rows = Math.Clamp(rows, RowsAtATime, MaxSets);
        while (Sets.Count < Rows)
        {
            Sets.Add(new SetRow());
        }
    }

    /// <summary>
    /// FIT-2, Q31-Q33: problems with a workout's rows as typed, for saving it as done, each naming its row. A blank row is
    /// ignored; a filled one needs an exercise and reps, and may have a weight (in <paramref name="unit"/>) and rest.
    /// </summary>
    public IEnumerable<ValidationResult> WorkoutProblems(WeightUnit unit)
    {
        if (Sets.Count(row => !row.IsBlank()) > MaxSets)
        {
            yield return new ValidationResult($"Up to {MaxSets} sets can be saved for one session.");
            yield break;
        }

        var unitName = unit == WeightUnit.Kilograms ? "kg" : "lb";
        var maxWeight = unit == WeightUnit.Kilograms ? 1000m : 2200m;
        for (var i = 0; i < Sets.Count; i++)
        {
            var row = Sets[i];
            if (row.IsBlank())
            {
                continue;
            }

            var number = i + 1;
            var box = $"{nameof(Sets)}[{i}].";
            if (row.Exercise is null)
            {
                yield return new ValidationResult($"Row {number}: enter the exercise, or clear the row.", [box + nameof(SetRow.Exercise)]);
            }
            else if (row.Exercise.Length > ExerciseNames.MaxLength)
            {
                yield return new ValidationResult($"Row {number}: the exercise's name must be {ExerciseNames.MaxLength} characters or fewer.", [box + nameof(SetRow.Exercise)]);
            }

            if (row.Reps is null && row.Exercise is not null)
            {
                yield return new ValidationResult($"Row {number}: enter the reps for {row.Exercise}.", [box + nameof(SetRow.Reps)]);
            }
            else if (row.Reps is < 1 or > MaxReps)
            {
                yield return new ValidationResult($"Row {number}: enter the reps as a whole number from 1 to 1,000.", [box + nameof(SetRow.Reps)]);
            }

            if (!string.IsNullOrWhiteSpace(row.Weight))
            {
                var weight = FormInput.ParseAmount(row.Weight);
                if (weight is null)
                {
                    yield return new ValidationResult($"Row {number}: enter the weight as a number, for example 100 or 102.5.", [box + nameof(SetRow.Weight)]);
                }
                else if (weight > maxWeight)
                {
                    yield return new ValidationResult(
                        string.Create(CultureInfo.InvariantCulture, $"Row {number}: enter a weight of at most {maxWeight:#,0} {unitName}."), [box + nameof(SetRow.Weight)]);
                }
            }

            if (row.Rest is < 0 or > MaxRestSeconds)
            {
                yield return new ValidationResult($"Row {number}: enter the rest in seconds, from 0 to 3,600 (an hour).", [box + nameof(SetRow.Rest)]);
            }
        }
    }

    /// <summary>
    /// FIT-2: replaces <paramref name="log"/>'s sets with the rows filled in, in order, weights in whole grams (Q33),
    /// each with its exercise from <paramref name="exerciseFor"/> (found by name, or added: Q30), and returns the new
    /// ones. Only call once <see cref="WorkoutProblems"/> finds none, with the log's sets loaded. The caller adds the new
    /// sets to the database itself: they have their ids already, so found only through the log they would be taken for
    /// sets already stored.
    /// </summary>
    public IReadOnlyList<SessionSet> ApplyWorkoutTo(SessionLog log, WeightUnit unit, Func<string, Exercise> exerciseFor)
    {
        log.Sets.Clear();
        foreach (var row in Sets.Where(row => !row.IsBlank()))
        {
            log.Sets.Add(new SessionSet
            {
                Position = log.Sets.Count,
                ExerciseId = exerciseFor(row.Exercise!).Id,
                Reps = row.Reps!.Value,
                WeightGrams = FormInput.ParseAmount(row.Weight) is { } amount ? Weights.Grams(amount, unit) : null,
                RestSeconds = row.Rest,
            });
        }

        return log.Sets;
    }

    // Each box from 0 to its most, then the whole: more than nothing and at most 24 hours.
    private IEnumerable<ValidationResult> TimeProblems()
    {
        if (TimeHours is null && TimeMinutes is null && TimeSeconds is null)
        {
            yield break;
        }

        var boxesFit = true;
        if (TimeHours is < 0 or > MaxHours)
        {
            boxesFit = false;
            yield return new ValidationResult($"Enter the hours as a whole number from 0 to {MaxHours}.", [nameof(TimeHours)]);
        }

        if (TimeMinutes is < 0 or > 59)
        {
            boxesFit = false;
            yield return new ValidationResult("Enter the minutes as a whole number from 0 to 59.", [nameof(TimeMinutes)]);
        }

        if (TimeSeconds is < 0 or > 59)
        {
            boxesFit = false;
            yield return new ValidationResult("Enter the seconds as a whole number from 0 to 59.", [nameof(TimeSeconds)]);
        }

        var seconds = ((TimeHours ?? 0) * 3600) + ((TimeMinutes ?? 0) * 60) + (TimeSeconds ?? 0);
        if (boxesFit && seconds == 0)
        {
            yield return new ValidationResult("Enter how long it took, or leave the hours, minutes and seconds blank.", [nameof(TimeHours)]);
        }
        else if (boxesFit && seconds > MaxHours * 3600)
        {
            yield return new ValidationResult($"Enter a time of at most {MaxHours} hours.", [nameof(TimeHours)]);
        }
    }

    // A custom type needs a name, of up to 40 characters (Q26, Q34).
    private static IEnumerable<ValidationResult> CustomTypeProblems(bool custom, string? name, string box)
    {
        if (!custom)
        {
            yield break;
        }

        if (name is null)
        {
            yield return new ValidationResult("Enter a name for the custom type, or choose another type.", [box]);
        }
        else if (name.Length > SessionLog.CustomRunTypeMaxLength)
        {
            yield return new ValidationResult($"The name of the custom type must be {SessionLog.CustomRunTypeMaxLength} characters or fewer.", [box]);
        }
    }

    // The time, distance and effort boxes onto the log, in seconds and whole metres (Q25).
    private void ApplyDetailsTo(SessionLog log, DistanceUnit unit)
    {
        log.ActualSeconds = TimeHours is null && TimeMinutes is null && TimeSeconds is null
            ? null
            : ((TimeHours ?? 0) * 3600) + ((TimeMinutes ?? 0) * 60) + (TimeSeconds ?? 0);
        log.DistanceMetres = FormInput.ParseAmount(Distance) is { } amount ? Distances.Metres(amount, unit) : null;
        log.Effort = Effort;
    }

    // A saved run's details in the boxes.
    private void FillRunBoxes(SessionLog log, DistanceUnit unit)
    {
        RunType = log.RunType;
        CustomRunType = log.CustomRunType;
        FillDetailBoxes(log, unit);
    }

    // The saved time, distance and effort in the boxes, the distance as text that saves back to the same metres (Q27).
    private void FillDetailBoxes(SessionLog log, DistanceUnit unit)
    {
        if (log.ActualSeconds is { } seconds)
        {
            (TimeHours, TimeMinutes, TimeSeconds) = (seconds / 3600, seconds / 60 % 60, seconds % 60);
        }

        Distance = log.DistanceMetres is { } metres ? Distances.InputText(metres, unit) : null;
        Effort = log.Effort;
    }

    // Q22: where its rule put it, before it was moved: "Tue 27 Oct, 07:00".
    private static string WhereItWas(Series series, LocalDate date, ClockFormat clock) =>
        $"{DayPattern.Format(date)}, {ClockText.At(date.At(series.StartTime), series.Zone, clock)}";
}
