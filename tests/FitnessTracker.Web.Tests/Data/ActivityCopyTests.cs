using System.Reflection;
using FitnessTracker.Web.Data;
using NodaTime;

namespace FitnessTracker.Web.Tests.Data;

// CAL-9: duplicating a routine copies its activities (Activity.CopyInto). Every detail of an activity must reach the
// copy, including any added later, so this fills every property and checks each one, apart from those a copy
// deliberately doesn't keep.
public class ActivityCopyTests
{
    // Its own identity, the routine it is copied into, and changes to single occurrences (on the original's dates).
    private static readonly string[] NotCopied =
    [
        nameof(Activity.Id), nameof(Activity.OwnerId), nameof(Activity.Version), nameof(Activity.RoutineId), nameof(Activity.OccurrenceChanges),
    ];

    [Fact]
    public void CopyInto_CopiesEveryDetail_ButItsIdentityAndSingleOccurrenceChanges()
    {
        var original = new Activity
        {
            OwnerId = "user-alex",
            Version = Guid.NewGuid(),
            Title = "Gym",
            Category = ActivityCategory.Gym,
            LocationId = Guid.NewGuid(),
            IsFixed = false,
            Priority = ActivityPriority.High,
            IsDemanding = true,
            PreparationMinutes = 10,
            ChangingMinutes = 15,
            Notes = "Leg day",
            StartDate = new LocalDate(2026, 10, 19),
            StartTime = new LocalTime(7, 15),
            TimeZoneId = "Europe/London",
            LengthMinutes = 80,
            RepeatRule = "FREQ=WEEKLY;BYDAY=MO",
            OccurrenceChanges = "2026-10-26:cancelled",
            GoalId = Guid.NewGuid(),
            RoutineId = Guid.NewGuid(),
            RoutineWeek = 'B',
        };
        var routine = Guid.NewGuid();

        var copy = original.CopyInto(routine);

        Assert.All(typeof(Activity).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(property => property.CanWrite && !NotCopied.Contains(property.Name)),
            property => Assert.True(Equals(property.GetValue(original), property.GetValue(copy)), $"{property.Name} wasn't copied."));
        Assert.NotEqual(original.Id, copy.Id);
        Assert.Equal(routine, copy.RoutineId);
        Assert.Null(copy.OccurrenceChanges);
    }

    // Guards the test above: a property added later would be left at its default there, which can't show whether it
    // was copied. This list fails first, as a reminder to give it a value above.
    [Fact]
    public void TheTestAbove_KnowsEveryProperty()
    {
        var properties = typeof(Activity).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanWrite && !NotCopied.Contains(property.Name))
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            ["Category", "ChangingMinutes", "GoalId", "IsDemanding", "IsFixed", "LengthMinutes", "LocationId", "Notes", "PreparationMinutes", "Priority", "RepeatRule", "RoutineWeek", "StartDate", "StartTime", "TimeZoneId", "Title"],
            properties);
    }
}
