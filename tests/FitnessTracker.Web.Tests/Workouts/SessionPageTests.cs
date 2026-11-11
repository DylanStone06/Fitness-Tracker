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

    // Code review of Phase 7: a save that doesn't say which button was pressed (a browser always does; a broken or made-up
    // request may not) is refused, and never taken for "back to planned", which would remove the session's log.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Finished")]
    public async Task ASaveWithNoMarkItCanRead_IsRefused_AndKeepsTheMark(string? mark)
    {
        var (browser, id) = await SignedInWithAGymSessionAsync("2026-10-19");
        await browser.MarkSessionAsync(id, "2026-10-19", "Done");
        var fields = new Dictionary<string, string> { ["Date"] = "2026-10-19", ["Notes"] = "" };
        if (mark is not null)
        {
            fields["Mark"] = mark;
        }

        var response = await browser.SubmitFormAsync($"/Sessions/Log/{id}?date=2026-10-19", $"/Sessions/Log/{id}", fields);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(SessionMark.Done, await StoredData.SessionMarkAsync(factory, id, Today));
    }

    // Requirements §9: a page opened before another tab marked the session can't overwrite that mark without saying so.
    // It says what the session is now, and saving from that page works.
    [Fact]
    public async Task APageOpenedBeforeAnotherTabMarkedIt_CantOverwriteTheMark()
    {
        var (browser, id) = await SignedInWithAGymSessionAsync("2026-10-19");
        var oldTab = await browser.LoadFormAsync($"/Sessions/Log/{id}?date=2026-10-19");
        await browser.MarkSessionAsync(id, "2026-10-19", "Done");

        var refused = await browser.SubmitAsync(oldTab, $"/Sessions/Log/{id}", new() { ["Date"] = "2026-10-19", ["Mark"] = "Skipped" });
        var page = WebUtility.HtmlDecode(await refused.Content.ReadAsStringAsync());
        var markAfterRefusal = await StoredData.SessionMarkAsync(factory, id, Today);
        var retried = await browser.SubmitAsync(TestBrowser.FormIn(page), $"/Sessions/Log/{id}", new() { ["Date"] = "2026-10-19", ["Mark"] = "Skipped" });

        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
        Assert.Contains("This session was changed in another tab or window after you opened this page. It's now marked done, with no notes.", page);
        Assert.Contains("Status: Done", page);
        Assert.Equal(SessionMark.Done, markAfterRefusal);
        Assert.Equal(HttpStatusCode.Redirect, retried.StatusCode);
        Assert.Equal(SessionMark.Skipped, await StoredData.SessionMarkAsync(factory, id, Today));
    }

    // ... nor put it back to planned: that would remove a mark it never showed.
    [Fact]
    public async Task APageOpenedBeforeAnotherTabChangedTheMark_CantPutItBackToPlanned()
    {
        var (browser, id) = await SignedInWithAGymSessionAsync("2026-10-19");
        await browser.MarkSessionAsync(id, "2026-10-19", "Done");
        var oldTab = await browser.LoadFormAsync($"/Sessions/Log/{id}?date=2026-10-19");
        await browser.MarkSessionAsync(id, "2026-10-19", "Skipped");

        var refused = await browser.SubmitAsync(oldTab, $"/Sessions/Log/{id}", new() { ["Date"] = "2026-10-19", ["Mark"] = "Planned" });

        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
        Assert.Contains(
            "This session was changed in another tab or window after you opened this page. It's now marked skipped, with no notes.",
            WebUtility.HtmlDecode(await refused.Content.ReadAsStringAsync()));
        Assert.Equal(SessionMark.Skipped, await StoredData.SessionMarkAsync(factory, id, Today));
    }

    // ... nor mark one the other tab put back to planned without saying so.
    [Fact]
    public async Task APageOpenedBeforeAnotherTabPutItBackToPlanned_SaysSo()
    {
        var (browser, id) = await SignedInWithAGymSessionAsync("2026-10-19");
        await browser.MarkSessionAsync(id, "2026-10-19", "Done");
        var oldTab = await browser.LoadFormAsync($"/Sessions/Log/{id}?date=2026-10-19");
        await browser.MarkSessionAsync(id, "2026-10-19", "Planned");

        var refused = await browser.SubmitAsync(oldTab, $"/Sessions/Log/{id}", new() { ["Date"] = "2026-10-19", ["Mark"] = "Skipped" });

        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
        Assert.Contains(
            "This session was changed in another tab or window after you opened this page. It's now back to planned. What you typed is still below",
            WebUtility.HtmlDecode(await refused.Content.ReadAsStringAsync()));
        Assert.Null(await StoredData.SessionMarkAsync(factory, id, Today));
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

    // Q21 (test review of Phase 7): whether a session today has started is judged at the moment it starts where it is.
    // Now is 13:00 in London (12:00 UTC), which is 08:00 in New York.
    [Theory]
    [InlineData("Europe/London", "12:30", true)]
    [InlineData("Europe/London", "13:30", false)]
    [InlineData("America/New_York", "07:30", true)]
    [InlineData("America/New_York", "08:30", false)]
    public async Task ASessionToday_CanBeMarkedDone_OnceItHasStartedWhereItIs(string timeZone, string start, bool started)
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        Assert.Equal(HttpStatusCode.Redirect, (await browser.SavePreferencesAsync(new() { ["TimeZoneId"] = timeZone })).StatusCode);
        var title = TestBrowser.NewName("Gym");
        Assert.Equal(HttpStatusCode.Redirect, (await browser.AddActivityAsync(title, new() { ["StartDate"] = "2026-10-19", ["StartTime"] = start })).StatusCode);
        var id = (await StoredData.ActivityTitledAsync(factory, title))!.Id;

        var response = await browser.MarkSessionAsync(id, "2026-10-19", "Done");

        Assert.Equal(started ? HttpStatusCode.Redirect : HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(started ? SessionMark.Done : null, await StoredData.SessionMarkAsync(factory, id, Today));
    }

    // Q21, Q22 (test review of Phase 7): a moved session has started by its new time, not its old one: tomorrow's,
    // moved to midday today, has; this morning's, moved to this evening, hasn't.
    [Theory]
    [InlineData("2026-10-20", "2026-10-19", "12:00", true)]
    [InlineData("2026-10-19", "2026-10-19", "18:00", false)]
    public async Task AMovedSession_CanBeMarkedDone_OnceItHasStartedAtItsNewTime(string date, string newDate, string newStart, bool started)
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var title = TestBrowser.NewName("Gym");
        Assert.Equal(HttpStatusCode.Redirect, (await browser.AddActivityAsync(title, new()
        {
            ["StartDate"] = "2026-10-19",
            ["Repeats"] = "Weekly",
            ["RepeatDays[0]"] = "true",
            ["RepeatDays[1]"] = "true",
        })).StatusCode);
        var id = (await StoredData.ActivityTitledAsync(factory, title))!.Id;
        Assert.Equal(HttpStatusCode.Redirect, (await browser.SubmitFormAsync($"/Activities/Occurrence/{id}?date={date}", $"/Activities/Occurrence/{id}", new()
        {
            ["Date"] = date,
            ["Change"] = "Move",
            ["NewDate"] = newDate,
            ["NewStartTime"] = newStart,
            ["DurationHours"] = "1",
            ["DurationMinutes"] = "0",
        })).StatusCode);

        var response = await browser.MarkSessionAsync(id, date, "Done");

        Assert.Equal(started ? HttpStatusCode.Redirect : HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(started ? SessionMark.Done : null, await StoredData.SessionMarkAsync(factory, id, LocalDate.FromDateTime(DateTime.Parse(date, System.Globalization.CultureInfo.InvariantCulture))));
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

    // The calendar's events say each session's status too ("Not logged", then "Done" once marked), and work's none.
    [Fact]
    public async Task TheCalendar_GivesEachSessionsStatus()
    {
        var (browser, gym) = await SignedInWithAGymSessionAsync("2026-10-19");
        var workTitle = TestBrowser.NewName("Work");
        Assert.Equal(HttpStatusCode.Redirect, (await browser.AddActivityAsync(workTitle, new() { ["StartDate"] = "2026-10-19", ["StartTime"] = "09:00", ["Category"] = "Work" })).StatusCode);
        var work = (await StoredData.ActivityTitledAsync(factory, workTitle))!.Id;

        var before = await CalendarStatusesAsync(browser);
        await browser.MarkSessionAsync(gym, "2026-10-19", "Done");
        var after = await CalendarStatusesAsync(browser);

        Assert.Equal("Not logged", before[gym]);
        Assert.Null(before[work]);
        Assert.Equal("Done", after[gym]);
    }

    // The page a calendar block opens links to the session's page: an occurrence's page for a repeating one, and a
    // one-off's form. Work's don't.
    [Fact]
    public async Task TheOccurrencePageAndAOneOffsForm_LinkToTheSessionsPage()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var weekly = TestBrowser.NewName("Gym");
        Assert.Equal(HttpStatusCode.Redirect, (await browser.AddActivityAsync(weekly, new() { ["StartDate"] = "2026-10-19", ["Repeats"] = "Weekly", ["RepeatDays[0]"] = "true" })).StatusCode);
        var weeklyId = (await StoredData.ActivityTitledAsync(factory, weekly))!.Id;
        var (oneOffBrowser, oneOff) = await SignedInWithAGymSessionAsync("2026-10-19");
        var (workBrowser, work) = await SignedInWithAGymSessionAsync("2026-10-19", category: "Work");

        var occurrencePage = await (await browser.GetAsync($"/Activities/Occurrence/{weeklyId}?date=2026-10-26")).Content.ReadAsStringAsync();
        var oneOffForm = await (await oneOffBrowser.GetAsync($"/Activities/Edit/{oneOff}")).Content.ReadAsStringAsync();
        var workForm = await (await workBrowser.GetAsync($"/Activities/Edit/{work}")).Content.ReadAsStringAsync();

        Assert.Contains($"href=\"/Sessions/Log/{weeklyId}?date=2026-10-26\"", occurrencePage);
        Assert.Contains($"href=\"/Sessions/Log/{oneOff}?date=2026-10-19\"", oneOffForm);
        Assert.DoesNotContain("/Sessions/Log/", workForm);
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

    // Monday 19 October's events' statuses, by activity.
    private static async Task<Dictionary<Guid, string?>> CalendarStatusesAsync(TestBrowser browser)
    {
        var response = await browser.GetAsync("/Calendar/Events?start=2026-10-19&end=2026-10-20");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.EnumerateArray()
            .Select(item => item.GetProperty("extendedProps"))
            .ToDictionary(details => details.GetProperty("activityId").GetGuid(), details => details.GetProperty("status").GetString());
    }

    private static async Task<string> PageAsync(TestBrowser browser, Guid id, string date)
    {
        var response = await browser.GetAsync($"/Sessions/Log/{id}?date={date}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }
}
