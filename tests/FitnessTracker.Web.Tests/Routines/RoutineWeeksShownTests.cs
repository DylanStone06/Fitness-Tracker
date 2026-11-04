using System.Net;
using System.Text.Json;
using FitnessTracker.Web.Tests.Infrastructure;

namespace FitnessTracker.Web.Tests.Routines;

// CAL-9: "each week shows its template and cycle week", on the calendar and on Plan my week, and Plan my week plans
// each week of a routine as its own (the roadmap's Phase 6b). Today in tests is Monday 19 October 2026; the two-week
// usual routine below has week A this week, so the week of 26 October is week B.
public sealed class RoutineWeeksShownTests(TestAppFactory factory) : IClassFixture<TestAppFactory>
{
    [Fact]
    public async Task TheCalendar_SaysWhichRoutineAndWeekTheDaysShownAreIn()
    {
        var (browser, _, name) = await SignedInWithABRoutineAsync();

        var weekA = await WeeksTextAsync(browser, "2026-10-19", "2026-10-26");
        var weekB = await WeeksTextAsync(browser, "2026-10-26T00:00:00", "2026-11-02T00:00:00");

        Assert.Equal($"{name}, week A", weekA);
        Assert.Equal($"{name}, week B", weekB);
    }

    // Days shown across two weeks of the cycle (a week starting on Sunday, say), each part named with its dates.
    [Fact]
    public async Task TheCalendar_NamesEachPart_WhenTheDaysShownAreInDifferentWeeks()
    {
        var (browser, _, name) = await SignedInWithABRoutineAsync();

        var text = await WeeksTextAsync(browser, "2026-10-25", "2026-11-01");

        Assert.Equal($"Sun 25 Oct: {name}, week A; Mon 26 Oct – Sat 31 Oct: {name}, week B", text);
    }

    [Fact]
    public async Task TheCalendar_SaysNothing_WithoutRoutines()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");

        Assert.Null(await WeeksTextAsync(browser, "2026-10-19", "2026-10-26"));
    }

    // T19: the feed is the signed-in user's only. Sam, with no routines, hears nothing of Alex's.
    [Fact]
    public async Task TheCalendarsRoutineFeed_IsOnlyTheSignedInUsersOwn()
    {
        await SignedInWithABRoutineAsync();
        var sam = await TestBrowser.OpenSignedInAsync(factory, "sam");

        Assert.Null(await WeeksTextAsync(sam, "2026-10-19", "2026-10-26"));
    }

    [Theory]
    [InlineData("", "2026-10-26")]
    [InlineData("2026-10-26", "2026-10-19")]
    [InlineData("2026-10-01", "2026-12-01")]
    public async Task TheCalendarsRoutineFeed_RefusesDatesItCantUse(string start, string end)
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");

        var response = await browser.GetAsync($"/Calendar/Weeks?start={start}&end={end}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PlanMyWeek_SaysWhichRoutineAndWeekItIs()
    {
        var (browser, _, name) = await SignedInWithABRoutineAsync();

        var html = WebUtility.HtmlDecode(await (await browser.GetAsync("/Plan?date=2026-10-26")).Content.ReadAsStringAsync());

        Assert.Contains($"Routine: {name}, week B", html);
    }

    // Planning week B builds week B: "Every week" adds the session to week B of the routine, and says so.
    [Fact]
    public async Task PlanMyWeek_AddsEachSessionToThatWeekOfTheRoutine()
    {
        var (browser, routine, _) = await SignedInWithABRoutineAsync();
        await browser.AddGoalAsync(TestBrowser.NewName("Gym"), new() { ["SessionsPerWeek"] = "1" });

        var html = WebUtility.HtmlDecode(await (await browser.GetAsync("/Plan?date=2026-10-26")).Content.ReadAsStringAsync());

        Assert.Contains($"<input type=\"hidden\" name=\"Weeks\" value=\"{routine}/B\" />", html);
        Assert.Contains(">Every week B<", html);
        Assert.Contains($"weeks={Uri.EscapeDataString($"{routine}/B")}", html);
    }

    [Fact]
    public async Task PlanMyWeek_WithoutRoutines_AddsEachSessionToEveryWeek()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        await browser.AddGoalAsync(TestBrowser.NewName("Gym"), new() { ["SessionsPerWeek"] = "1" });

        var html = WebUtility.HtmlDecode(await (await browser.GetAsync("/Plan?date=2026-10-26")).Content.ReadAsStringAsync());

        Assert.Contains("<input type=\"hidden\" name=\"Weeks\" value=\"\" />", html);
        Assert.Contains(">Every week<", html);
        Assert.DoesNotContain("Routine:", html);
    }

    // "Change" opens the activity form with the same weeks chosen.
    [Fact]
    public async Task ChangingASuggestion_OpensTheFormWithThatWeekChosen()
    {
        var (browser, routine, _) = await SignedInWithABRoutineAsync();
        var goalTitle = TestBrowser.NewName("Gym");
        await browser.AddGoalAsync(goalTitle, new() { ["SessionsPerWeek"] = "1" });
        var goal = (await StoredData.GoalTitledAsync(factory, goalTitle))!.Id;

        var html = WebUtility.HtmlDecode(await (await browser.GetAsync(
            $"/Activities/Create?goal={goal}&date=2026-10-27&time=18:00&everyWeek=true&returnToPlan=true&weeks={Uri.EscapeDataString($"{routine}/B")}")).Content.ReadAsStringAsync());

        Assert.Contains($"<option selected=\"selected\" value=\"{routine}/B\">Week B only</option>", html);
    }

    // A goal's session in week A only counts towards the goal in week A: week B still needs all of them.
    [Fact]
    public async Task AGoalsSessionInWeekA_CountsOnlyInWeekA()
    {
        var (browser, routine, _) = await SignedInWithABRoutineAsync();
        var goalTitle = TestBrowser.NewName("Gym");
        await browser.AddGoalAsync(goalTitle);
        var goal = (await StoredData.GoalTitledAsync(factory, goalTitle))!.Id;
        var added = await browser.AddActivityAsync(TestBrowser.NewName("Gym"), new()
        {
            ["StartDate"] = "2026-10-19",
            ["Repeats"] = "Weekly",
            ["RepeatDays[0]"] = "true",
            ["GoalId"] = goal.ToString(),
            ["Weeks"] = $"{routine}/A",
        });
        Assert.Equal(HttpStatusCode.Redirect, added.StatusCode);

        var weekA = await (await browser.GetAsync("/Plan?date=2026-10-19")).Content.ReadAsStringAsync();
        var weekB = await (await browser.GetAsync("/Plan?date=2026-10-26")).Content.ReadAsStringAsync();

        Assert.Contains("1 of 3 already planned", weekA);
        Assert.DoesNotContain("already planned", weekB);
    }

    // A two-week usual routine with week A this week (19 October 2026).
    private async Task<(TestBrowser Browser, Guid Routine, string Name)> SignedInWithABRoutineAsync()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var name = TestBrowser.NewName("Usual week");
        var added = await browser.AddRoutineAsync(name, new() { ["CycleWeeks"] = "2", ["WeekA"] = "2026-10-19", ["IsUsual"] = "true" });
        Assert.Equal(HttpStatusCode.Redirect, added.StatusCode);
        return (browser, (await StoredData.RoutineNamedAsync(factory, name))!.Id, name);
    }

    private static async Task<string?> WeeksTextAsync(TestBrowser browser, string start, string end)
    {
        var response = await browser.GetAsync($"/Calendar/Weeks?start={Uri.EscapeDataString(start)}&end={Uri.EscapeDataString(end)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var text = json.RootElement.GetProperty("text");
        return text.ValueKind == JsonValueKind.Null ? null : text.GetString();
    }
}
