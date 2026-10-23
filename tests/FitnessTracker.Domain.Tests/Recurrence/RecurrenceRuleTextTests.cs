using FitnessTracker.Domain.Recurrence;
using NodaTime;

namespace FitnessTracker.Domain.Tests.Recurrence;

// ADR 0001: repeat rules are stored as iCalendar RRULE text (RFC 5545), limited to the subset the app supports
// (CAL-3: daily or weekly on chosen days, every N, ending on a date, after a count, or never). One canonical form
// is written; reading accepts that form with its parts in any order and refuses anything outside the subset.
public class RecurrenceRuleTextTests
{
    public static TheoryData<string> CanonicalRules => new()
    {
        "FREQ=DAILY;INTERVAL=1",
        "FREQ=DAILY;INTERVAL=3;COUNT=10",
        "FREQ=DAILY;INTERVAL=2;UNTIL=20270328",
        "FREQ=WEEKLY;INTERVAL=1;BYDAY=MO,WE,FR;WKST=MO",
        "FREQ=WEEKLY;INTERVAL=2;BYDAY=TU;WKST=SU;UNTIL=20261231",
        "FREQ=WEEKLY;INTERVAL=1;BYDAY=SA,SU;WKST=MO;COUNT=4",
    };

    // Reading then writing gives back exactly the text that was stored, so nothing is lost in between.
    [Theory]
    [MemberData(nameof(CanonicalRules))]
    public void Rules_RoundTripThroughText(string text)
    {
        Assert.Equal(text, RecurrenceRuleText.Format(RecurrenceRuleText.Parse(text)));
    }

    [Fact]
    public void Format_WritesAWeeklyRule_WithItsDaysInWeekOrder_AndItsOwnWeekStart()
    {
        var rule = RecurrenceRule.Weekly([IsoDayOfWeek.Friday, IsoDayOfWeek.Monday, IsoDayOfWeek.Wednesday], everyWeeks: 2, weekStart: IsoDayOfWeek.Sunday)
            .EndingOn(new LocalDate(2026, 12, 31));

        Assert.Equal("FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,WE,FR;WKST=SU;UNTIL=20261231", RecurrenceRuleText.Format(rule));
    }

    [Fact]
    public void Parse_ReadsEveryPart_InAnyOrder()
    {
        var rule = RecurrenceRuleText.Parse("COUNT=6;BYDAY=TH,TU;WKST=MO;INTERVAL=2;FREQ=WEEKLY");

        Assert.Equal(RepeatFrequency.Weekly, rule.Frequency);
        Assert.Equal(2, rule.Every);
        Assert.Equal(new HashSet<IsoDayOfWeek> { IsoDayOfWeek.Tuesday, IsoDayOfWeek.Thursday }, rule.Days.ToHashSet());
        Assert.Equal(IsoDayOfWeek.Monday, rule.WeekStart);
        Assert.Equal(6, rule.Count);
        Assert.Null(rule.LastDate);
    }

    // Hand-written or imported text: spaces around day codes, lower case, and the "RRULE:" prefix used inside .ics
    // files are all read the same way as the canonical form.
    [Theory]
    [InlineData("FREQ=WEEKLY;BYDAY=MO, WE ,FR")]
    [InlineData("freq=weekly;byday=mo,we,fr")]
    [InlineData("RRULE:FREQ=WEEKLY;BYDAY=MO,WE,FR")]
    [InlineData("rrule:FREQ=WEEKLY;BYDAY=MO,WE,FR")]
    [InlineData("FREQ = WEEKLY;BYDAY=MO,WE,FR")]
    [InlineData("FREQ= WEEKLY;BYDAY=MO,WE,FR")]
    [InlineData("FREQ=WEEKLY;INTERVAL= 1;BYDAY=MO,WE,FR")]
    [InlineData("FREQ=WEEKLY;BYDAY=MO,WE,FR;WKST= MO")]
    public void Parse_ReadsLooselyWrittenText_AsTheCanonicalRule(string text)
    {
        Assert.Equal("FREQ=WEEKLY;INTERVAL=1;BYDAY=MO,WE,FR;WKST=MO", RecurrenceRuleText.Format(RecurrenceRuleText.Parse(text)));
    }

    // What was read behaves exactly like the rule it was written from: a fortnightly Tuesday rule from Tuesday
    // 6 October 2026 falls on 6 and 20 October, not 13.
    [Fact]
    public void ARuleReadFromText_FallsOnTheSameDates()
    {
        var rule = RecurrenceRuleText.Parse("FREQ=WEEKLY;INTERVAL=2;BYDAY=TU;WKST=MO");

        Assert.Equal(
            [new LocalDate(2026, 10, 6), new LocalDate(2026, 10, 20)],
            rule.Dates(firstDate: new LocalDate(2026, 10, 6), from: new LocalDate(2026, 10, 1), through: new LocalDate(2026, 10, 31)));
    }

    [Theory]
    [InlineData("", "FREQ")]
    [InlineData("INTERVAL=2", "FREQ")]
    [InlineData("FREQ=MONTHLY;INTERVAL=1", "MONTHLY")]
    [InlineData("FREQ=DAILY;INTERVAL=1;BYDAY=MO", "BYDAY")]
    [InlineData("FREQ=WEEKLY;INTERVAL=1;WKST=MO", "BYDAY")]
    [InlineData("FREQ=WEEKLY;INTERVAL=1;BYDAY=XX;WKST=MO", "XX")]
    [InlineData("FREQ=DAILY;INTERVAL=0", "INTERVAL")]
    [InlineData("FREQ=DAILY;INTERVAL=1;COUNT=3;UNTIL=20261231", "COUNT")]
    [InlineData("FREQ=DAILY;INTERVAL=1;UNTIL=2026-12-31", "UNTIL")]
    [InlineData("FREQ=DAILY;INTERVAL=1;BYMONTHDAY=1", "BYMONTHDAY")]
    [InlineData("FREQ=DAILY;FREQ=WEEKLY", "FREQ")]
    [InlineData("FREQ=DAILY;freq=WEEKLY", "FREQ")]
    [InlineData("FREQ=DAILY;INTERVAL=1;WKST=XX", "XX")]
    [InlineData("FREQ=WEEKLY;INTERVAL=1;BYDAY=MO;WKST=XX", "XX")]
    [InlineData("FREQ=WEEKLY;BYDAY=1MO", "1MO")]
    [InlineData("FREQ=DAILY;COUNT=0", "COUNT")]
    [InlineData("FREQ=DAILY;COUNT=-1", "COUNT")]
    [InlineData("FREQ=DAILY;INTERVAL=99999999999", "INTERVAL")]
    [InlineData("FREQ=DAILY;UNTIL=20260229", "UNTIL")]
    [InlineData("FREQ=DAILY;UNTIL=20261231T235959Z", "UNTIL")]
    // An empty value names its part, not just the empty text.
    [InlineData("FREQ=WEEKLY;BYDAY=", "BYDAY")]
    [InlineData("FREQ=WEEKLY;BYDAY=MO,", "BYDAY")]
    [InlineData("FREQ=WEEKLY;BYDAY=MO;WKST=", "WKST")]
    [InlineData("FREQ=", "FREQ")]
    [InlineData("=DAILY", "=DAILY")]
    public void Parse_RefusesTextOutsideTheSubset_NamingWhatIsWrong(string text, string named)
    {
        var error = Assert.Throws<FormatException>(() => RecurrenceRuleText.Parse(text));

        Assert.Contains(named, error.Message);
    }

    // A number too big to store is too big, not "less than 1"; and 0 is too small, not too big.
    [Theory]
    [InlineData("FREQ=DAILY;INTERVAL=99999999999", true)]
    [InlineData("FREQ=DAILY;COUNT=0", false)]
    [InlineData("FREQ=DAILY;COUNT=00", false)]
    public void Parse_SaysANumberIsTooBig_OnlyWhenItIs(string text, bool tooBig)
    {
        var error = Assert.Throws<FormatException>(() => RecurrenceRuleText.Parse(text));

        Assert.Equal(tooBig, error.Message.Contains("too big", StringComparison.Ordinal));
    }

    // A daily rule doesn't use a week start, so it isn't written; and days are always written Monday first, whatever
    // the week start, so one rule has one text.
    [Fact]
    public void Format_LeavesOutAWeekStart_ThatADailyRuleDoesNotUse()
    {
        Assert.Equal("FREQ=DAILY;INTERVAL=1", RecurrenceRuleText.Format(RecurrenceRuleText.Parse("FREQ=DAILY;INTERVAL=1;WKST=SU")));
    }

    [Fact]
    public void Format_WritesDaysMondayFirst_EvenWhenTheWeekStartsOnSunday()
    {
        var rule = RecurrenceRule.Weekly([IsoDayOfWeek.Sunday, IsoDayOfWeek.Monday], weekStart: IsoDayOfWeek.Sunday);

        Assert.Equal("FREQ=WEEKLY;INTERVAL=1;BYDAY=MO,SU;WKST=SU", RecurrenceRuleText.Format(rule));
    }

    // RFC 5545 section 3.8.5.3's own example: from Tuesday 5 August 1997, every other week on Tuesday and Sunday, four
    // times. The week start read from the text changes which weeks count, giving the RFC's two different answers.
    [Theory]
    [InlineData("MO", new[] { 5, 10, 19, 24 })]
    [InlineData("SU", new[] { 5, 17, 19, 31 })]
    public void ARuleReadFromText_KeepsItsWeekStart_AsInRfc5545(string weekStart, int[] augustDays)
    {
        var rule = RecurrenceRuleText.Parse($"FREQ=WEEKLY;INTERVAL=2;COUNT=4;BYDAY=TU,SU;WKST={weekStart}");

        Assert.Equal(
            augustDays.Select(day => new LocalDate(1997, 8, day)),
            rule.Dates(firstDate: new LocalDate(1997, 8, 5), from: new LocalDate(1997, 8, 1), through: new LocalDate(1997, 9, 30)));
    }

    // UNTIL is the last date on the series' own clock (§8), not in UTC: 23:30 in New York on Sunday 25 October 2026
    // is already the 26th in UTC, and still counts.
    [Fact]
    public void Until_IncludesTheWholeLastLocalDay_NotTheUtcDay()
    {
        var newYork = DateTimeZoneProviders.Tzdb["America/New_York"];
        var series = new Series(new LocalDate(2026, 10, 24), new LocalTime(23, 30), Duration.FromHours(1), newYork,
            RecurrenceRuleText.Parse("FREQ=DAILY;INTERVAL=1;UNTIL=20261025"));

        Assert.Equal(
            [Instant.FromUtc(2026, 10, 25, 3, 30), Instant.FromUtc(2026, 10, 26, 3, 30)],
            series.OccurrencesBetween(new LocalDate(2026, 10, 24), new LocalDate(2026, 10, 31)).Select(occurrence => occurrence.Time.Start));
    }

    // T4 with an end date: the clocks go back in London on Sunday 25 October 2026, so 01:30 happens twice that night.
    // The last occurrence is still on the 25th, at the first 01:30 (§8).
    [Fact]
    public void Until_OnTheNightTheClocksGoBack_EndsWithTheFirst0130()
    {
        var london = DateTimeZoneProviders.Tzdb["Europe/London"];
        var series = new Series(new LocalDate(2026, 10, 23), new LocalTime(1, 30), Duration.FromMinutes(30), london,
            RecurrenceRuleText.Parse("FREQ=DAILY;INTERVAL=1;UNTIL=20261025"));

        Assert.Equal(
            [Instant.FromUtc(2026, 10, 23, 0, 30), Instant.FromUtc(2026, 10, 24, 0, 30), Instant.FromUtc(2026, 10, 25, 0, 30)],
            series.OccurrencesBetween(new LocalDate(2026, 10, 20), new LocalDate(2026, 10, 31)).Select(occurrence => occurrence.Time.Start));
    }
}
