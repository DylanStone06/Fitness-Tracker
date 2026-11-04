using System.Net;
using System.Text.RegularExpressions;
using FitnessTracker.Web.Tests.Infrastructure;

namespace FitnessTracker.Web.Tests.Goals;

// SCH-6, T16: other times later in the week for one session (a missed or skipped one, or one to move), with nothing
// else moved. Each opens the occurrence's page, or a one-off's form, with that time filled in, to check and confirm
// (the same pages a drag on the calendar opens, CAL-8). The tests' clock is Monday 19 October 2026; the week of
// Monday 2 November is ahead.
public sealed partial class ReschedulePageTests(TestAppFactory factory) : IClassFixture<TestAppFactory>
{
    [Fact]
    public async Task AnOccurrence_GetsOtherTimesThatWeek_ThatOpenItsPageFilledIn()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var title = TestBrowser.NewName("Gym");
        await browser.AddActivityAsync(title, new() { ["StartDate"] = "2026-11-02", ["StartTime"] = "18:00", ["Repeats"] = "Weekly", ["RepeatDays[0]"] = "true" });
        var id = (await StoredData.ActivityTitledAsync(factory, title))!.Id;

        var html = await PageAsync(browser, $"/Activities/Reschedule/{id}?date=2026-11-02");

        var moves = MoveLinks().Matches(html).Select(match => Uri.UnescapeDataString(WebUtility.HtmlDecode(match.Groups[1].Value))).ToList();
        Assert.Equal(3, moves.Count);
        Assert.All(moves, move => Assert.Matches($"""^/Activities/Occurrence/{id}\?date=2026-11-02&start=2026-11-0[2-8]T\d\d:\d\d&minutes=60""", move));
        Assert.DoesNotContain(moves, move => move.Contains("start=2026-11-02T18:00", StringComparison.Ordinal));
    }

    // SCH-6, §7.3: one session of a goal a day, as the planner keeps: a missed session of a daily series isn't offered
    // on a day that already has one of its sessions (scheduling review, Phase 6: its own series wasn't counted).
    [Fact]
    public async Task OtherTimes_AreNotOnADayThatAlreadyHasTheSession()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var title = TestBrowser.NewName("Gym");
        await browser.AddActivityAsync(title, new() { ["StartDate"] = "2026-11-02", ["StartTime"] = "18:00", ["Repeats"] = "Daily" });
        var id = (await StoredData.ActivityTitledAsync(factory, title))!.Id;

        var html = await PageAsync(browser, $"/Activities/Reschedule/{id}?date=2026-11-02");

        var moves = MoveLinks().Matches(html).Select(match => Uri.UnescapeDataString(WebUtility.HtmlDecode(match.Groups[1].Value))).ToList();
        Assert.NotEmpty(moves);
        Assert.All(moves, move => Assert.Contains("start=2026-11-02T", move, StringComparison.Ordinal));
    }

    // A one-off is moved on its own form.
    [Fact]
    public async Task AOneOff_GetsOtherTimes_ThatOpenItsFormFilledIn()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var title = TestBrowser.NewName("Swim");
        await browser.AddActivityAsync(title, new() { ["StartDate"] = "2026-11-04", ["StartTime"] = "07:00" });
        var id = (await StoredData.ActivityTitledAsync(factory, title))!.Id;

        var html = await PageAsync(browser, $"/Activities/Reschedule/{id}?date=2026-11-04");

        Assert.Matches($"""href="/Activities/Edit/{id}\?start=2026-11-0[2-8]T\d\d:\d\d&minutes=60""", Uri.UnescapeDataString(WebUtility.HtmlDecode(html)));
    }

    // The occurrence's page links here.
    [Fact]
    public async Task TheOccurrencePage_LinksToOtherTimes()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var title = TestBrowser.NewName("Gym");
        await browser.AddActivityAsync(title, new() { ["StartDate"] = "2026-11-02", ["Repeats"] = "Weekly", ["RepeatDays[0]"] = "true" });
        var id = (await StoredData.ActivityTitledAsync(factory, title))!.Id;

        var html = await PageAsync(browser, $"/Activities/Occurrence/{id}?date=2026-11-09");

        Assert.Contains($"href=\"/Activities/Reschedule/{id}?date=2026-11-09\"", html);
    }

    // From the calendar, the move goes back to the calendar once saved (code review, Phase 6: it ended on the list).
    [Fact]
    public async Task FromTheCalendar_TheMoveGoesBackToIt()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var title = TestBrowser.NewName("Gym");
        await browser.AddActivityAsync(title, new() { ["StartDate"] = "2026-11-02", ["Repeats"] = "Weekly", ["RepeatDays[0]"] = "true" });
        var id = (await StoredData.ActivityTitledAsync(factory, title))!.Id;

        var occurrence = await PageAsync(browser, $"/Activities/Occurrence/{id}?date=2026-11-09&returnToCalendar=true");
        var html = await PageAsync(browser, $"/Activities/Reschedule/{id}?date=2026-11-09&returnToCalendar=true");

        Assert.Contains($"href=\"/Activities/Reschedule/{id}?date=2026-11-09&amp;returnToCalendar=true\"", occurrence);
        Assert.All(MoveLinks().Matches(html), move => Assert.EndsWith("&amp;returnToCalendar=true", move.Groups[1].Value));
        Assert.NotEmpty(MoveLinks().Matches(html));
    }

    // A date it doesn't happen on is "not found", like an occurrence's page.
    [Fact]
    public async Task ADateItDoesntHappenOn_IsNotFound()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var title = TestBrowser.NewName("Gym");
        await browser.AddActivityAsync(title, new() { ["StartDate"] = "2026-11-02", ["Repeats"] = "Weekly", ["RepeatDays[0]"] = "true" });
        var id = (await StoredData.ActivityTitledAsync(factory, title))!.Id;

        var response = await browser.GetAsync($"/Activities/Reschedule/{id}?date=2026-11-03");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<string> PageAsync(TestBrowser browser, string url)
    {
        var response = await browser.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    [GeneratedRegex("href=\"(/Activities/Occurrence/[^\"]*&amp;start=[^\"]*)\"")]
    private static partial Regex MoveLinks();
}
