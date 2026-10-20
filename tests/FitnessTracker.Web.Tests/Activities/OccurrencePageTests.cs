using System.Net;
using System.Text.RegularExpressions;
using FitnessTracker.Web.Tests.Infrastructure;

namespace FitnessTracker.Web.Tests.Activities;

// CAL-4: changing one occurrence of a repeating activity without touching the others (P5): move or resize it, cancel
// it, or put it back. Every change goes through the check on save (SCH-1), which reports only what the change itself
// breaks. "Gym" is on Mondays and Wednesdays at 07:00 for an hour, from Monday 2 November 2026 (GMT).
public sealed class OccurrencePageTests(TestAppFactory factory) : IClassFixture<TestAppFactory>
{
    [Fact]
    public async Task ThePage_ShowsTheOccurrence_ReadyToChange()
    {
        var (browser, id, title) = await GymAsync();

        var html = await PageAsync(browser, $"/Activities/Occurrence/{id}?date=2026-11-04");

        Assert.Contains(title, html);
        Assert.Contains("Wed 4 Nov 2026 at 07:00 for 1 h", html);
        Assert.Equal("2026-11-04", InputValue(html, "NewDate"));
        Assert.Equal("07:00", InputValue(html, "NewStartTime"));
        Assert.Equal("1", InputValue(html, "DurationHours"));
        Assert.Contains($"href=\"/Activities/Edit/{id}\"", html);
    }

    [Fact]
    public async Task CancellingJustThisOne_LeavesTheOthers()
    {
        var (browser, id, title) = await GymAsync();

        var response = await ChangeAsync(browser, id, "2026-11-04", new() { ["Change"] = "Cancel" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("""[{"date":"2026-11-04","cancelled":true}]""", (await StoredData.ActivityTitledAsync(factory, title))!.OccurrenceChanges);
    }

    [Fact]
    public async Task MovingJustThisOne_StoresItsNewTimeAndLength()
    {
        var (browser, id, title) = await GymAsync();

        await ChangeAsync(browser, id, "2026-11-04", new()
        {
            ["Change"] = "Move",
            ["NewDate"] = "2026-11-05",
            ["NewStartTime"] = "18:30",
            ["DurationHours"] = "0",
            ["DurationMinutes"] = "45",
        });

        Assert.Equal(
            """[{"date":"2026-11-04","start":"2026-11-05T18:30","minutes":45}]""",
            (await StoredData.ActivityTitledAsync(factory, title))!.OccurrenceChanges);
    }

    [Fact]
    public async Task PuttingItBack_UndoesTheChange()
    {
        var (browser, id, title) = await GymAsync();
        await ChangeAsync(browser, id, "2026-11-04", new() { ["Change"] = "Cancel" });

        var page = await PageAsync(browser, $"/Activities/Occurrence/{id}?date=2026-11-04");
        Assert.Contains("Cancelled", page);
        await ChangeAsync(browser, id, "2026-11-04", new() { ["Change"] = "Restore" });

        Assert.Null((await StoredData.ActivityTitledAsync(factory, title))!.OccurrenceChanges);
    }

    // H1, Q2: a move into a clash is shown and needs "Save anyway", like any save.
    [Fact]
    public async Task AMoveIntoAClash_IsShown_AndNeedsSaveAnyway()
    {
        var (browser, id, title) = await GymAsync();
        await browser.AddActivityAsync("Dentist", new() { ["StartDate"] = "2026-11-05", ["StartTime"] = "18:00" });
        var move = new Dictionary<string, string> { ["Change"] = "Move", ["NewDate"] = "2026-11-05", ["NewStartTime"] = "18:30" };

        var shown = await ChangeAsync(browser, id, "2026-11-04", move);

        Assert.Equal(HttpStatusCode.OK, shown.StatusCode);
        var html = await shown.Content.ReadAsStringAsync();
        Assert.Contains("Dentist", html);
        Assert.Null((await StoredData.ActivityTitledAsync(factory, title))!.OccurrenceChanges);

        var confirmed = await browser.SubmitAsync(TestBrowser.FormIn(html), $"/Activities/Occurrence/{id}", new(Form(id, "2026-11-04", move)) { ["ConfirmClashes"] = "true" });
        Assert.Equal(HttpStatusCode.Redirect, confirmed.StatusCode);
        Assert.NotNull((await StoredData.ActivityTitledAsync(factory, title))!.OccurrenceChanges);
    }

    // Only what this change breaks is reported: Mondays already clash with a team meeting (kept with "Save anyway"), and
    // that doesn't stop cancelling a Wednesday.
    [Fact]
    public async Task AClashTheSeriesAlreadyHad_DoesntStopAChangeElsewhere()
    {
        var (browser, id, title) = await GymAsync();
        var meeting = TestBrowser.ActivityForm("Team meeting", new()
        {
            ["StartDate"] = "2026-11-02",
            ["StartTime"] = "07:30",
            ["Repeats"] = "Weekly",
            ["RepeatDays[0]"] = "true",
        });
        var shown = TestBrowser.FormIn(await (await browser.SubmitFormAsync("/Activities/Create", "/Activities/Create", meeting)).Content.ReadAsStringAsync());
        await browser.SubmitAsync(shown, "/Activities/Create", new(meeting) { ["ConfirmClashes"] = "true" });

        var response = await ChangeAsync(browser, id, "2026-11-04", new() { ["Change"] = "Cancel" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull((await StoredData.ActivityTitledAsync(factory, title))!.OccurrenceChanges);
    }

    [Theory]
    [InlineData("NewDate", "")]
    [InlineData("NewStartTime", "25:00")]
    [InlineData("DurationMinutes", "0")]
    public async Task AMoveThatCantBeRight_IsRefused(string field, string value)
    {
        var (browser, id, title) = await GymAsync();
        var move = new Dictionary<string, string> { ["Change"] = "Move", ["DurationHours"] = "0", ["DurationMinutes"] = "30", [field] = value };

        var response = await ChangeAsync(browser, id, "2026-11-04", move);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<title>Error: ", await response.Content.ReadAsStringAsync());
        Assert.Null((await StoredData.ActivityTitledAsync(factory, title))!.OccurrenceChanges);
    }

    // §9: changed in another tab since the page opened: not saved, and the page says why.
    [Fact]
    public async Task AChangeFromAnOutOfDateTab_IsNotSaved()
    {
        var (browser, id, title) = await GymAsync();
        var oldTab = TestBrowser.FormIn(await PageAsync(browser, $"/Activities/Occurrence/{id}?date=2026-11-04"));
        await ChangeAsync(browser, id, "2026-11-09", new() { ["Change"] = "Cancel" });

        var response = await browser.SubmitAsync(oldTab, $"/Activities/Occurrence/{id}", Form(id, "2026-11-04", new() { ["Change"] = "Cancel" }, version: null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("changed in another tab", await response.Content.ReadAsStringAsync());
        Assert.Equal("""[{"date":"2026-11-09","cancelled":true}]""", (await StoredData.ActivityTitledAsync(factory, title))!.OccurrenceChanges);
    }

    // A date the activity doesn't fall on has no occurrence to change.
    [Fact]
    public async Task ADateItDoesntHappenOn_IsNotFound()
    {
        var (browser, id, _) = await GymAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync($"/Activities/Occurrence/{id}?date=2026-11-05")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync($"/Activities/Occurrence/{id}")).StatusCode);
    }

    // A one-off has only one occurrence: it is changed with the ordinary form.
    [Fact]
    public async Task AOneOff_GoesToTheOrdinaryForm()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var title = TestBrowser.NewName("Dentist");
        await browser.AddActivityAsync(title, new() { ["StartDate"] = "2026-11-05" });
        var id = (await StoredData.ActivityTitledAsync(factory, title))!.Id;

        var response = await browser.GetAsync($"/Activities/Occurrence/{id}?date=2026-11-05&returnToCalendar=true");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/Activities/Edit/{id}?returnToCalendar=true", response.Headers.Location?.OriginalString);
    }

    // CAL-8: dragging on the calendar opens this page with the new time filled in, to check and confirm.
    [Fact]
    public async Task ADraggedTime_IsFilledIn()
    {
        var (browser, id, _) = await GymAsync();

        var html = await PageAsync(browser, $"/Activities/Occurrence/{id}?date=2026-11-04&start=2026-11-05T18:30&minutes=45&returnToCalendar=true");

        Assert.Equal("2026-11-05", InputValue(html, "NewDate"));
        Assert.Equal("18:30", InputValue(html, "NewStartTime"));
        Assert.Equal("0", InputValue(html, "DurationHours"));
        Assert.Equal("45", InputValue(html, "DurationMinutes"));
    }

    [Fact]
    public async Task FromTheCalendar_ItGoesBackToTheCalendar_OnTheNewDate()
    {
        var (browser, id, _) = await GymAsync();

        var response = await ChangeAsync(browser, id, "2026-11-04", new()
        {
            ["Change"] = "Move",
            ["NewDate"] = "2026-11-12",
            ["ReturnToCalendar"] = "true",
        });

        Assert.Equal("/Calendar?date=2026-11-12", response.Headers.Location?.OriginalString);
    }

    private async Task<(TestBrowser Browser, Guid Id, string Title)> GymAsync()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var title = TestBrowser.NewName("Gym");
        await browser.AddActivityAsync(title, new()
        {
            ["StartDate"] = "2026-11-02",
            ["StartTime"] = "07:00",
            ["Repeats"] = "Weekly",
            ["RepeatDays[0]"] = "true",
            ["RepeatDays[2]"] = "true",
        });
        return (browser, (await StoredData.ActivityTitledAsync(factory, title))!.Id, title);
    }

    // Posts the occurrence form as the page fills it, with the given changes.
    private static Task<HttpResponseMessage> ChangeAsync(TestBrowser browser, Guid id, string date, Dictionary<string, string> changes) =>
        browser.SubmitFormAsync($"/Activities/Occurrence/{id}?date={date}", $"/Activities/Occurrence/{id}", Form(id, date, changes, version: null));

    // version null: the one the page carries (SubmitFormAsync and SubmitAsync send the page's hidden fields).
    private static Dictionary<string, string> Form(Guid id, string date, Dictionary<string, string> changes, string? version = null)
    {
        var form = new Dictionary<string, string>
        {
            ["Date"] = date,
            ["NewDate"] = date,
            ["NewStartTime"] = "07:00",
            ["DurationHours"] = "1",
            ["DurationMinutes"] = "0",
        };
        if (version is not null)
        {
            form["Version"] = version;
        }

        foreach (var (name, value) in changes)
        {
            form[name] = value;
        }

        return form;
    }

    private static async Task<string> PageAsync(TestBrowser browser, string url)
    {
        var response = await browser.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private static string? InputValue(string html, string name)
    {
        var input = Regex.Match(html, $"""<input[^>]*\sname="{name}"[^>]*>""");
        var value = Regex.Match(input.Value, """\svalue="([^"]*)" """.TrimEnd());
        return value.Success ? WebUtility.HtmlDecode(value.Groups[1].Value) : null;
    }
}
