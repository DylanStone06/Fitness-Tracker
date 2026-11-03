using System.Net;
using FitnessTracker.Domain.Workouts;
using FitnessTracker.Web.Tests.Infrastructure;
using NodaTime;

namespace FitnessTracker.Web.Tests.Workouts;

// FIT-1, Q20-Q22: a session (one occurrence of an exercise activity) can be marked done once it has started, or
// skipped at any time, and put back to planned; it is never marked done by itself (T20). Today in tests is Monday
// 19 October 2026, 13:00 in London: a gym session at 07:00 today is over, one tomorrow is still to come.
public sealed class SessionPageTests(TestAppFactory factory) : IClassFixture<TestAppFactory>
{
    private static readonly LocalDate Today = new(2026, 10, 19);

    [Fact]
    public async Task APastSession_CanBeMarkedDone()
    {
        var (browser, id) = await SignedInWithAGymSessionAsync("2026-10-19");

        var response = await browser.MarkSessionAsync(id, "2026-10-19", "Done");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(SessionMark.Done, await StoredData.SessionMarkAsync(factory, id, Today));
        Assert.Contains("Status: Done", await PageAsync(browser, id, "2026-10-19"));
    }

    // T20: its time has passed and it isn't marked, so it says "Not logged", and is still planned: nothing is stored.
    [Fact]
    public async Task T20_APastSessionNotMarked_SaysNotLogged_AndStaysPlanned()
    {
        var (browser, id) = await SignedInWithAGymSessionAsync("2026-10-19");

        var page = await PageAsync(browser, id, "2026-10-19");

        Assert.Contains("Status: Planned", page);
        Assert.Contains("Not logged", page);
        Assert.Null(await StoredData.SessionMarkAsync(factory, id, Today));
    }

    // Q21: one still to come can't be marked done, but can be skipped ("not going tomorrow").
    [Fact]
    public async Task ASessionStillToCome_CantBeMarkedDone_ButCanBeSkipped()
    {
        var (browser, id) = await SignedInWithAGymSessionAsync("2026-10-20");

        var done = await browser.MarkSessionAsync(id, "2026-10-20", "Done");
        var markAfterDone = await StoredData.SessionMarkAsync(factory, id, Today.PlusDays(1));
        var skipped = await browser.MarkSessionAsync(id, "2026-10-20", "Skipped");

        Assert.Equal(HttpStatusCode.OK, done.StatusCode);
        Assert.Contains("It hasn't started yet, so it can't be marked done.", WebUtility.HtmlDecode(await done.Content.ReadAsStringAsync()));
        Assert.Null(markAfterDone);
        Assert.Equal(HttpStatusCode.Redirect, skipped.StatusCode);
        Assert.Equal(SessionMark.Skipped, await StoredData.SessionMarkAsync(factory, id, Today.PlusDays(1)));
    }

    // Q21: a mark can be undone, back to planned.
    [Fact]
    public async Task AMark_CanBeUndone()
    {
        var (browser, id) = await SignedInWithAGymSessionAsync("2026-10-19");
        await browser.MarkSessionAsync(id, "2026-10-19", "Done");

        var response = await browser.MarkSessionAsync(id, "2026-10-19", "Planned");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Null(await StoredData.SessionMarkAsync(factory, id, Today));
        Assert.Contains("Status: Planned", await PageAsync(browser, id, "2026-10-19"));
    }

    // Q22: one moved on its own to another time is rescheduled, and says where it was and where it is now.
    [Fact]
    public async Task ARescheduledSession_SaysWhereItWas()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var title = TestBrowser.NewName("Gym");
        Assert.Equal(HttpStatusCode.Redirect, (await browser.AddActivityAsync(title, new()
        {
            ["StartDate"] = "2026-10-20",
            ["Repeats"] = "Weekly",
            ["RepeatDays[1]"] = "true",
        })).StatusCode);
        var id = (await StoredData.ActivityTitledAsync(factory, title))!.Id;
        Assert.Equal(HttpStatusCode.Redirect, (await browser.SubmitFormAsync($"/Activities/Occurrence/{id}?date=2026-10-27", $"/Activities/Occurrence/{id}", new()
        {
            ["Date"] = "2026-10-27",
            ["Change"] = "Move",
            ["NewDate"] = "2026-10-29",
            ["NewStartTime"] = "18:00",
            ["DurationHours"] = "1",
            ["DurationMinutes"] = "0",
        })).StatusCode);

        var page = await PageAsync(browser, id, "2026-10-27");

        Assert.Contains("Status: Rescheduled from Tue 27 Oct, 07:00", page);
        Assert.Contains("Thu 29 Oct, 18:00", page);
    }

    // The home page shows each session's status (T20: "Not logged" for this morning's), linking to its page.
    [Fact]
    public async Task TheHomePage_ShowsEachSessionsStatus_AndLinksToItsPage()
    {
        var (browser, past) = await SignedInWithAGymSessionAsync("2026-10-19");
        var done = TestBrowser.NewName("Gym");
        Assert.Equal(HttpStatusCode.Redirect, (await browser.AddActivityAsync(done, new() { ["StartDate"] = "2026-10-19", ["StartTime"] = "09:00" })).StatusCode);
        var doneId = (await StoredData.ActivityTitledAsync(factory, done))!.Id;
        await browser.MarkSessionAsync(doneId, "2026-10-19", "Done");
        var tomorrow = TestBrowser.NewName("Gym");
        Assert.Equal(HttpStatusCode.Redirect, (await browser.AddActivityAsync(tomorrow, new() { ["StartDate"] = "2026-10-20" })).StatusCode);

        var html = WebUtility.HtmlDecode(await (await browser.GetAsync("/")).Content.ReadAsStringAsync());

        Assert.Contains("Status: Not logged", html);
        Assert.Contains("Status: Done", html);
        Assert.Contains("Status: Planned", html);
        Assert.Contains($"href=\"/Sessions/Log/{past}?date=2026-10-19\"", html);
    }

    // ... and only for exercise: work has no status.
    [Fact]
    public async Task TheHomePage_ShowsNoStatusForWork()
    {
        var (browser, _) = await SignedInWithAGymSessionAsync("2026-10-19", category: "Work");

        Assert.DoesNotContain("Status:", await (await browser.GetAsync("/")).Content.ReadAsStringAsync());
    }

    // Q20: only exercise sessions have a status: work has no session page.
    [Fact]
    public async Task OnlyExerciseSessions_HaveAPage()
    {
        var (browser, id) = await SignedInWithAGymSessionAsync("2026-10-19", category: "Work");

        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync($"/Sessions/Log/{id}?date=2026-10-19")).StatusCode);
    }

    [Fact]
    public async Task ADateItDoesntHappenOn_IsNotFound()
    {
        var (browser, id) = await SignedInWithAGymSessionAsync("2026-10-19");

        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync($"/Sessions/Log/{id}?date=2026-10-20")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.MarkSessionAsync(id, "2026-10-20", "Skipped", pageDate: "2026-10-19")).StatusCode);
    }

    // T19: another user's session is "not found", and isn't marked.
    [Fact]
    public async Task AnotherUsersSession_IsNotFound_AndNotMarked()
    {
        var (_, alexs) = await SignedInWithAGymSessionAsync("2026-10-19");
        var (sam, sams) = await SignedInWithAGymSessionAsync("2026-10-19", user: "sam");

        var response = await sam.SubmitFormAsync($"/Sessions/Log/{sams}?date=2026-10-19", $"/Sessions/Log/{alexs}", new() { ["Date"] = "2026-10-19", ["Mark"] = "Done" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(await StoredData.SessionMarkAsync(factory, alexs, Today));
    }

    private async Task<(TestBrowser Browser, Guid Id)> SignedInWithAGymSessionAsync(string date, string category = "Gym", string user = "alex")
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, user);
        var title = TestBrowser.NewName(category);
        Assert.Equal(HttpStatusCode.Redirect, (await browser.AddActivityAsync(title, new() { ["StartDate"] = date, ["Category"] = category })).StatusCode);
        return (browser, (await StoredData.ActivityTitledAsync(factory, title))!.Id);
    }

    private static async Task<string> PageAsync(TestBrowser browser, Guid id, string date)
    {
        var response = await browser.GetAsync($"/Sessions/Log/{id}?date={date}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }
}
