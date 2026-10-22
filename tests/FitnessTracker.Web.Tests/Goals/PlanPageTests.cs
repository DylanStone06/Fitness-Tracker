using System.Net;
using System.Text.RegularExpressions;
using FitnessTracker.Web.Tests.Infrastructure;

namespace FitnessTracker.Web.Tests.Goals;

// SCH-3, SCH-4, SCH-5: the "Plan my week" page. It suggests times for the sessions weekly goals (GOL-1) still need;
// each can be added every week or just that week (Q12), changed first, or said no to. Adding one goes through the
// ordinary activity form and its check on save (SCH-1). What doesn't fit is explained. The tests' clock is Monday
// 19 October 2026, so the week of Monday 2 November is planned (no clock change, nothing over).
public sealed partial class PlanPageTests(TestAppFactory factory) : IClassFixture<TestAppFactory>
{
    private const string Week = "/Plan?date=2026-11-02";

    [Fact]
    public async Task WithNoGoals_ThePageSaysWhatToDoFirst()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");

        var html = await PageAsync(browser, Week);

        Assert.Contains("Add the things you want to do each week", html);
        Assert.Contains("href=\"/Goals/Create\"", html);
    }

    [Fact]
    public async Task AGoal_GetsASuggestionForEachSession_WithWaysToAnswer()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var title = TestBrowser.NewName("Gym");
        await browser.AddGoalAsync(title);

        var html = await PageAsync(browser, Week);

        Assert.Contains("2 – 8 Nov 2026", html);
        Assert.Equal(3, AddForms(html).Count(form => form.Contains("value=\"Weekly\"")));
        Assert.Equal(3, AddForms(html).Count(form => form.Contains("name=\"Repeats\" value=\"Never\"")));
        Assert.All(AddForms(html), form => Assert.Contains("name=\"ReturnToPlan\" value=\"true\"", form));
        Assert.Equal(3, Regex.Matches(html, """href="/Activities/Create\?goal=""").Count);
        Assert.Equal(3, Regex.Matches(html, """href="/Plan\?date=2026-11-02&amp;skip=""").Count);
    }

    // SCH-4, Q12: "Every week" adds the session every week on that day, for the goal, through the check on save, and
    // the plan then counts it: two sessions left to find.
    [Fact]
    public async Task AddingASuggestionEveryWeek_SavesItForTheGoal_AndThePlanCountsIt()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var title = TestBrowser.NewName("Gym");
        await browser.AddGoalAsync(title);
        var goal = (await StoredData.GoalTitledAsync(factory, title))!.Id;
        var form = AddForms(await PageAsync(browser, Week)).First(form => form.Contains("value=\"Weekly\""));

        var response = await browser.SubmitFormAsync(Week, "/Activities/Create", FieldsOf(form));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Plan?date=2026-11-0", response.Headers.Location?.OriginalString);
        var saved = await StoredData.ActivityTitledAsync(factory, title);
        Assert.NotNull(saved);
        Assert.Equal(goal, saved.GoalId);
        Assert.StartsWith("FREQ=WEEKLY", saved.RepeatRule);
        Assert.False(saved.IsFixed);
        var after = await PageAsync(browser, Week);
        Assert.Contains("1 of 3 already planned", after);
        Assert.Equal(2, AddForms(after).Count(form => form.Contains("value=\"Weekly\"")));
    }

    // SCH-4: "No" plans again without that time.
    [Fact]
    public async Task SayingNo_PlansAgainWithoutThatTime()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        await browser.AddGoalAsync(TestBrowser.NewName("Gym"), new() { ["SessionsPerWeek"] = "1" });
        var html = await PageAsync(browser, Week);
        var no = WebUtility.HtmlDecode(Regex.Match(html, """href="(/Plan\?date=2026-11-02&amp;skip=[^"]+)" """.TrimEnd()).Groups[1].Value);
        var firstStart = InputValue(AddForms(html).First(), "StartDate") + " " + InputValue(AddForms(html).First(), "StartTime");

        var again = await PageAsync(browser, no);

        var secondStart = InputValue(AddForms(again).First(), "StartDate") + " " + InputValue(AddForms(again).First(), "StartTime");
        Assert.NotEqual(firstStart, secondStart);
    }

    // SCH-5, P1: a goal that can't be planned says why.
    [Fact]
    public async Task AGoalThatCantBePlanned_SaysWhy()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var home = TestBrowser.NewName("Home");
        var pool = TestBrowser.NewName("Pool");
        await browser.AddLocationAsync(home, new() { ["IsHome"] = "true" });
        await browser.AddLocationAsync(pool);
        var poolId = (await StoredData.LocationNamedAsync(factory, pool))!.Id;
        await browser.AddGoalAsync(TestBrowser.NewName("Swim"), new() { ["LocationId"] = poolId.ToString() });

        var html = await PageAsync(browser, Week);

        Assert.Contains($"There&#x27;s no travel time from {home} to {pool}", html);
    }

    // ACC-7: an exercise goal with no preferred days of its own uses the days in Preferences (here Saturday only).
    [Fact]
    public async Task AnExerciseGoalWithoutItsOwnDays_UsesThePreferredExerciseDays()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        Assert.Equal(HttpStatusCode.Redirect, (await browser.SavePreferencesAsync(new() { ["PreferredExerciseDays"] = "Saturday" })).StatusCode);
        await browser.AddGoalAsync(TestBrowser.NewName("Gym"), new() { ["SessionsPerWeek"] = "1" });

        var html = await PageAsync(browser, Week);

        Assert.Equal("2026-11-07", InputValue(AddForms(html).First(), "StartDate"));
    }

    // H5 (scheduling review H-1): with 96 hours between demanding sessions, a hard ride on Thursday 29 October at 18:00,
    // four days before the week, is loaded and kept apart from: Monday 18:00 at the earliest, not Monday morning.
    [Fact]
    public async Task ADemandingSessionDaysBeforeTheWeek_IsKeptApartFrom()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        Assert.Equal(HttpStatusCode.Redirect, (await browser.SavePreferencesAsync(new() { ["MinimumHoursBetweenDemandingSessions"] = "96" })).StatusCode);
        await browser.AddActivityAsync(TestBrowser.NewName("Hard ride"), new() { ["StartDate"] = "2026-10-29", ["StartTime"] = "18:00", ["IsDemanding"] = "true" });
        await browser.AddGoalAsync(TestBrowser.NewName("Intervals"), new() { ["SessionsPerWeek"] = "1", ["IsDemanding"] = "true", ["DurationMinutes"] = "45", ["DurationHours"] = "0" });

        var form = AddForms(await PageAsync(browser, Week)).First();

        Assert.Equal(("2026-11-02", "18:00"), (InputValue(form, "StartDate"), InputValue(form, "StartTime")));
    }

    // CAL-7: each day's free time as the calendar is, without sleep (23:00-07:00, with 30 minutes either side).
    [Fact]
    public async Task EachDaysFreeTime_IsShown()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        await browser.AddActivityAsync(TestBrowser.NewName("Dentist"), new() { ["StartDate"] = "2026-11-03", ["StartTime"] = "12:00" });
        await browser.AddGoalAsync(TestBrowser.NewName("Gym"));

        var html = await PageAsync(browser, Week);

        Assert.Contains("Mon 2 Nov: 15 h free, the longest 15 h", html);
        // 07:30-12:00 and 13:00-22:30 around the dentist at 12:00.
        Assert.Contains("Tue 3 Nov: 14 h free, the longest 9 h 30 min", html);
    }

    // T19: only the signed-in user's goals are planned.
    [Fact]
    public async Task AnotherUsersGoals_AreNeverShown()
    {
        var alex = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var alexsGoal = TestBrowser.NewName("Alex gym");
        await alex.AddGoalAsync(alexsGoal);
        var sam = await TestBrowser.OpenSignedInAsync(factory, "sam");

        var html = await PageAsync(sam, Week);

        Assert.DoesNotContain(alexsGoal, html);
    }

    private static async Task<string> PageAsync(TestBrowser browser, string url)
    {
        var response = await browser.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    // Every form on the page that adds a session through the activity form.
    private static List<string> AddForms(string html) =>
        AddForm().Matches(html).Select(match => match.Value).ToList();

    // The fields a form sends, as the page fills them (the anti-forgery token is added by SubmitFormAsync).
    private static Dictionary<string, string> FieldsOf(string form) =>
        Regex.Matches(form, """<input[^>]*>""")
            .Select(input => (Name: Attribute(input.Value, "name"), Value: Attribute(input.Value, "value")))
            .Where(field => field.Name is not null and not "__RequestVerificationToken")
            .GroupBy(field => field.Name!)
            .ToDictionary(group => group.Key, group => group.First().Value ?? "");

    private static string? InputValue(string html, string name)
    {
        var input = Regex.Match(html, $"""<input[^>]*\sname="{Regex.Escape(name)}"[^>]*>""");
        return input.Success ? Attribute(input.Value, "value") : null;
    }

    private static string? Attribute(string tag, string name)
    {
        var value = Regex.Match(tag, $"""\s{name}="([^"]*)" """.TrimEnd());
        return value.Success ? WebUtility.HtmlDecode(value.Groups[1].Value) : null;
    }

    [GeneratedRegex("""<form[^>]*action="/Activities/Create"[^>]*>.*?</form>""", RegexOptions.Singleline)]
    private static partial Regex AddForm();
}
