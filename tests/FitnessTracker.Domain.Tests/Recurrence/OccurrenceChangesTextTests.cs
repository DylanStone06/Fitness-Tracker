using FitnessTracker.Domain.Recurrence;
using NodaTime;

namespace FitnessTracker.Domain.Tests.Recurrence;

// CAL-4: a series' one-off changes (cancelled, or moved and resized) stored as text with the activity, in one canonical
// form, and read back strictly: anything the app didn't write is refused with a reason, never half-read.
public class OccurrenceChangesTextTests
{
    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];

    // Gym on Mondays and Wednesdays at 07:00 for an hour, from Monday 2 November 2026.
    private static readonly Series Gym = new(
        new LocalDate(2026, 11, 2), new LocalTime(7, 0), Duration.FromHours(1), London,
        RecurrenceRule.Weekly([IsoDayOfWeek.Monday, IsoDayOfWeek.Wednesday]));

    [Fact]
    public void NoChanges_AreNoText()
    {
        Assert.Null(OccurrenceChangesText.Format(Gym.Changes));
        Assert.Empty(OccurrenceChangesText.Apply(Gym, null).Changes);
        Assert.Empty(OccurrenceChangesText.Apply(Gym, "").Changes);
    }

    [Fact]
    public void Changes_AreWrittenInDateOrder_AndReadBack()
    {
        var changed = Gym
            .MoveOccurrence(new LocalDate(2026, 11, 11), new LocalDateTime(2026, 11, 11, 19, 0), Duration.FromMinutes(90))
            .CancelOccurrence(new LocalDate(2026, 11, 4));

        var text = OccurrenceChangesText.Format(changed.Changes);

        Assert.Equal(
            """[{"date":"2026-11-04","cancelled":true},{"date":"2026-11-11","start":"2026-11-11T19:00","minutes":90}]""",
            text);
        Assert.Equal(changed.Changes, OccurrenceChangesText.Apply(Gym, text).Changes);
    }

    // A move to another day is stored by its new date and time; the occurrence is still known by its own date.
    [Fact]
    public void AMoveToAnotherDay_KeepsItsOwnDate()
    {
        var text = """[{"date":"2026-11-04","start":"2026-11-05T18:30","minutes":60}]""";

        var occurrence = Assert.Single(OccurrenceChangesText.Apply(Gym, text).OccurrencesBetween(new LocalDate(2026, 11, 5), new LocalDate(2026, 11, 5)));

        Assert.Equal(new LocalDate(2026, 11, 4), occurrence.Date);
        Assert.Equal(Instant.FromUtc(2026, 11, 5, 18, 30), occurrence.Time.Start);
    }

    [Theory]
    [InlineData("not json", "can't be read")]
    [InlineData("""{"date":"2026-11-04","cancelled":true}""", "a list")]
    [InlineData("""[{"cancelled":true}]""", "date")]
    [InlineData("""[{"date":"4 Nov 2026","cancelled":true}]""", "date")]
    [InlineData("""[{"date":"2026-11-04"}]""", "neither cancelled nor moved")]
    [InlineData("""[{"date":"2026-11-04","cancelled":true,"start":"2026-11-04T08:00","minutes":60}]""", "both cancelled and moved")]
    [InlineData("""[{"date":"2026-11-04","start":"2026-11-04T08:00"}]""", "minutes")]
    [InlineData("""[{"date":"2026-11-04","start":"2026-11-04T08:00","minutes":0}]""", "minutes")]
    [InlineData("""[{"date":"2026-11-04","start":"8am","minutes":60}]""", "start")]
    [InlineData("""[{"date":"2026-11-04","cancelled":true},{"date":"2026-11-04","cancelled":true}]""", "twice")]
    [InlineData("""[{"date":"2026-11-05","cancelled":true}]""", "Thu 5 Nov 2026")]
    // Scheduling review L-4: nothing else either, so nothing is quietly ignored.
    [InlineData("""[{"date":"2026-11-04","cancelled":true,"minutes":90}]""", "minutes")]
    [InlineData("""[{"date":"2026-11-04","cancelled":true,"note":"ill"}]""", "note")]
    [InlineData("""[{"date":"2026-11-04","cancelled":false}]""", "cancelled")]
    public void TextTheAppDidntWrite_IsRefused_WithTheReason(string text, string reason)
    {
        var error = Assert.Throws<FormatException>(() => OccurrenceChangesText.Apply(Gym, text));

        Assert.Contains(reason, error.Message);
    }

    // §8: moves into the hour the clocks skip and the hour they repeat read back as the series' own rules say: 01:30 on
    // 28 March 2027 is 02:30 BST (01:30 UTC); 01:30 on 25 October 2026 is the first, BST (00:30 UTC).
    [Theory]
    [InlineData("2027-03-28T01:30", 2027, 3, 28, 1, 30)]
    [InlineData("2026-10-25T01:30", 2026, 10, 25, 0, 30)]
    public void AMoveOnAClockChangeNight_ReadsBackAsTheSeriesResolvesIt(string start, int year, int month, int day, int hour, int minute)
    {
        var text = $$"""[{"date":"2026-11-04","start":"{{start}}","minutes":30}]""";

        var occurrence = Assert.Single(OccurrenceChangesText.Apply(Gym, text).OccurrencesBetween(new LocalDate(year, month, day), new LocalDate(year, month, day)));

        Assert.Equal(Instant.FromUtc(year, month, day, hour, minute), occurrence.Time.Start);
        Assert.Equal(text, OccurrenceChangesText.Format(OccurrenceChangesText.Apply(Gym, text).Changes));
    }

    [Fact]
    public void ALengthThatIsntWholeMinutes_CantBeWritten()
    {
        var changed = Gym.MoveOccurrence(new LocalDate(2026, 11, 4), new LocalDateTime(2026, 11, 4, 8, 0), Duration.FromSeconds(90));

        Assert.Throws<ArgumentException>(() => OccurrenceChangesText.Format(changed.Changes));
    }
}
