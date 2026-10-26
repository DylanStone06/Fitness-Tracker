using System.Net;
using System.Text.Json;
using FitnessTracker.Web.Tests.Infrastructure;

namespace FitnessTracker.Web.Tests.Routines;

// CAL-9: a repeating activity happens every week, or only while one routine is in use, or only in one week of its
// cycle (A/B weeks). Today in tests is Monday 19 October 2026; the two-week usual routine below has week A this week,
// so the week of 26 October is week B.
public sealed class ActivitiesInRoutinesTests(TestAppFactory factory) : IClassFixture<TestAppFactory>
{
    [Fact]
    public async Task Add_SavesTheRoutineAndTheWeek()
    {
        var (browser, routine) = await SignedInWithABRoutineAsync();
        var title = TestBrowser.NewName("Gym");

        var response = await browser.AddActivityAsync(title, MondaysFromToday(new() { ["Weeks"] = $"{routine}/B" }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var saved = await StoredData.ActivityTitledAsync(factory, title);
        Assert.Equal(routine, saved!.RoutineId);
        Assert.Equal('B', saved.RoutineWeek);
    }

    [Fact]
    public async Task Add_InEveryWeekOfARoutine_SavesNoWeek()
    {
        var (browser, routine) = await SignedInWithABRoutineAsync();
        var title = TestBrowser.NewName("Gym");

        await browser.AddActivityAsync(title, MondaysFromToday(new() { ["Weeks"] = routine.ToString() }));

        var saved = await StoredData.ActivityTitledAsync(factory, title);
        Assert.Equal(routine, saved!.RoutineId);
        Assert.Null(saved.RoutineWeek);
    }

    // The weeks only mean something for an activity that repeats, as the repeat's other boxes do.
    [Fact]
    public async Task AOneOff_IsNeverInARoutine()
    {
        var (browser, routine) = await SignedInWithABRoutineAsync();
        var title = TestBrowser.NewName("Dentist");

        await browser.AddActivityAsync(title, new() { ["Weeks"] = $"{routine}/A" });

        var saved = await StoredData.ActivityTitledAsync(factory, title);
        Assert.Null(saved!.RoutineId);
        Assert.Null(saved.RoutineWeek);
    }

    // CLAUDE.md rule 5 (architecture rule 5): a routine must be the user's own; another user's is "not found" by the
    // filter, and reported like a choice that isn't on the list.
    [Fact]
    public async Task Add_RefusesAnotherUsersRoutine()
    {
        var (_, alexsRoutine) = await SignedInWithABRoutineAsync();
        var sam = await TestBrowser.OpenSignedInAsync(factory, "sam");
        var title = TestBrowser.NewName("Gym");

        var response = await sam.AddActivityAsync(title, MondaysFromToday(new() { ["Weeks"] = alexsRoutine.ToString() }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Choose which weeks it happens in from the list.", await response.Content.ReadAsStringAsync());
        Assert.Null(await StoredData.ActivityTitledAsync(factory, title));
    }

    [Theory]
    [InlineData("C")]
    [InlineData("a")]
    [InlineData("AB")]
    public async Task Add_RefusesAWeekTheRoutineDoesNotHave(string week)
    {
        var (browser, routine) = await SignedInWithABRoutineAsync();
        var title = TestBrowser.NewName("Gym");

        var response = await browser.AddActivityAsync(title, MondaysFromToday(new() { ["Weeks"] = $"{routine}/{week}" }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Choose which weeks it happens in from the list.", await response.Content.ReadAsStringAsync());
        Assert.Null(await StoredData.ActivityTitledAsync(factory, title));
    }

    // "After 6 times" would count the weeks it's left out of too: 6 Mondays, but only 3 of them in week A.
    [Fact]
    public async Task Add_RefusesEndingAfterANumberOfTimes_InARoutine()
    {
        var (browser, routine) = await SignedInWithABRoutineAsync();
        var title = TestBrowser.NewName("Gym");

        var response = await browser.AddActivityAsync(title, MondaysFromToday(new()
        {
            ["Weeks"] = $"{routine}/A",
            ["Ends"] = "AfterCount",
            ["EndCount"] = "6",
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("can't end after a number of times", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        Assert.Null(await StoredData.ActivityTitledAsync(factory, title));
    }

    [Fact]
    public async Task TheCalendar_ShowsAWeekAActivity_InWeekAOnly()
    {
        var (browser, routine) = await SignedInWithABRoutineAsync();
        var title = TestBrowser.NewName("Gym");
        await browser.AddActivityAsync(title, MondaysFromToday(new() { ["Weeks"] = $"{routine}/A" }));

        var weekA = await EventsAsync(browser, "2026-10-19", "2026-10-26");
        var weekB = await EventsAsync(browser, "2026-10-26", "2026-11-02");
        var nextWeekA = await EventsAsync(browser, "2026-11-02", "2026-11-09");

        Assert.Equal("2026-10-19T07:00:00", Assert.Single(weekA).GetProperty("start").GetString());
        Assert.Empty(weekB);
        Assert.Equal("2026-11-02T07:00:00", Assert.Single(nextWeekA).GetProperty("start").GetString());
    }

    // SCH-1: the check on save sees the weeks too. Week A's and week B's Monday sessions at the same time never meet;
    // one every week meets week A's.
    [Fact]
    public async Task TheCheckOnSave_OnlyFindsClashesInTheWeeksBothHappen()
    {
        var (browser, routine) = await SignedInWithABRoutineAsync();
        await browser.AddActivityAsync(TestBrowser.NewName("Week A gym"), MondaysFromToday(new() { ["Weeks"] = $"{routine}/A" }));

        var weekB = await browser.AddActivityAsync(TestBrowser.NewName("Week B run"), MondaysFromToday(new() { ["Weeks"] = $"{routine}/B" }));
        var everyWeek = await browser.AddActivityAsync(TestBrowser.NewName("Swim"), MondaysFromToday());

        Assert.Equal(HttpStatusCode.Redirect, weekB.StatusCode);
        Assert.Equal(HttpStatusCode.OK, everyWeek.StatusCode);
        Assert.Contains("It clashes with your other activities", await everyWeek.Content.ReadAsStringAsync());
    }

    // CAL-4: a week it isn't in has no occurrence to change.
    [Fact]
    public async Task TheOccurrencePage_ForAWeekItIsNotIn_IsNotFound()
    {
        var (browser, routine) = await SignedInWithABRoutineAsync();
        var title = TestBrowser.NewName("Gym");
        await browser.AddActivityAsync(title, MondaysFromToday(new() { ["Weeks"] = $"{routine}/A" }));
        var id = (await StoredData.ActivityTitledAsync(factory, title))!.Id;

        var weekA = await browser.GetAsync($"/Activities/Occurrence/{id}?date=2026-10-19");
        var weekB = await browser.GetAsync($"/Activities/Occurrence/{id}?date=2026-10-26");

        Assert.Equal(HttpStatusCode.OK, weekA.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, weekB.StatusCode);
    }

    // CAL-4, P5: from every week to week A only, a one-off change in week B is on a day it no longer happens: removed,
    // and the user is told, as when the repeat days change.
    [Fact]
    public async Task ChangingToOneWeek_RemovesChangesInTheOtherWeek_AndSaysSo()
    {
        var (browser, routine) = await SignedInWithABRoutineAsync();
        var title = TestBrowser.NewName("Gym");
        await browser.AddActivityAsync(title, MondaysFromToday());
        var id = (await StoredData.ActivityTitledAsync(factory, title))!.Id;
        var cancelled = await browser.SubmitFormAsync($"/Activities/Occurrence/{id}?date=2026-10-26", $"/Activities/Occurrence/{id}",
            new() { ["Date"] = "2026-10-26", ["Change"] = "Cancel" });
        Assert.Equal(HttpStatusCode.Redirect, cancelled.StatusCode);

        var edited = await browser.SubmitFormAsync($"/Activities/Edit/{id}", $"/Activities/Edit/{id}",
            TestBrowser.ActivityForm(title, MondaysFromToday(new() { ["Weeks"] = $"{routine}/A" })));
        var list = await (await browser.GetAsync("/Activities")).Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Redirect, edited.StatusCode);
        Assert.Null((await StoredData.ActivityTitledAsync(factory, title))!.OccurrenceChanges);
        Assert.Contains("1 change was on a day it no longer happens, so it was removed", list);
    }

    // ... and from every week of a routine to its week A only, the same (scheduling review, Phase 6b: the week was read
    // after the form had changed it, so the week B change was kept, hidden, and said to be kept).
    [Fact]
    public async Task ChangingFromEveryWeekOfARoutineToWeekA_RemovesWeekBChanges_AndSaysSo()
    {
        var (browser, routine) = await SignedInWithABRoutineAsync();
        var title = TestBrowser.NewName("Gym");
        await browser.AddActivityAsync(title, MondaysFromToday(new() { ["Weeks"] = routine.ToString() }));
        var id = (await StoredData.ActivityTitledAsync(factory, title))!.Id;
        var cancelled = await browser.SubmitFormAsync($"/Activities/Occurrence/{id}?date=2026-10-26", $"/Activities/Occurrence/{id}",
            new() { ["Date"] = "2026-10-26", ["Change"] = "Cancel" });
        Assert.Equal(HttpStatusCode.Redirect, cancelled.StatusCode);

        var edited = await browser.SubmitFormAsync($"/Activities/Edit/{id}", $"/Activities/Edit/{id}",
            TestBrowser.ActivityForm(title, MondaysFromToday(new() { ["Weeks"] = $"{routine}/A" })));
        var list = await (await browser.GetAsync("/Activities")).Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Redirect, edited.StatusCode);
        Assert.Null((await StoredData.ActivityTitledAsync(factory, title))!.OccurrenceChanges);
        Assert.Contains("1 change was on a day it no longer happens, so it was removed", list);
    }

    [Fact]
    public async Task TheList_SaysWhichWeeksEachActivityIsIn()
    {
        var (browser, routine, name) = await SignedInWithNamedABRoutineAsync();
        await browser.AddActivityAsync(TestBrowser.NewName("Gym"), MondaysFromToday(new() { ["Weeks"] = $"{routine}/B" }));
        await browser.AddActivityAsync(TestBrowser.NewName("Run"), MondaysFromToday(new() { ["StartTime"] = "18:00", ["Weeks"] = routine.ToString() }));

        var html = WebUtility.HtmlDecode(await (await browser.GetAsync("/Activities")).Content.ReadAsStringAsync());

        Assert.Contains($"Week B of {name} only", html);
        Assert.Contains($"Only while {name} is in use", html);
    }

    // The form offers the user's routines and their weeks, and keeps the activity's choice when it is edited.
    [Fact]
    public async Task TheForm_OffersEachRoutinesWeeks_AndShowsTheActivitysChoice()
    {
        var (browser, routine, name) = await SignedInWithNamedABRoutineAsync();
        var title = TestBrowser.NewName("Gym");
        await browser.AddActivityAsync(title, MondaysFromToday(new() { ["Weeks"] = $"{routine}/B" }));
        var id = (await StoredData.ActivityTitledAsync(factory, title))!.Id;

        var html = WebUtility.HtmlDecode(await (await browser.GetAsync($"/Activities/Edit/{id}")).Content.ReadAsStringAsync());

        Assert.Contains($"<optgroup label=\"{name}\">", html);
        Assert.Contains($"<option value=\"{routine}\">Whenever {name} is in use</option>", html);
        Assert.Contains($"<option value=\"{routine}/A\">Week A only</option>", html);
        Assert.Contains($"<option selected=\"selected\" value=\"{routine}/B\">Week B only</option>", html);
    }

    // P1: only the usual routine is in use until routines can be chosen for dates, so an activity in another one never
    // happens. The form and the list say so, rather than it just disappearing.
    [Fact]
    public async Task ARoutineNotInUse_IsSaidToBe_OnTheFormAndTheList()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var holiday = TestBrowser.NewName("Holiday");
        await browser.AddRoutineAsync(holiday);
        var routine = (await StoredData.RoutineNamedAsync(factory, holiday))!.Id;
        await browser.AddActivityAsync(TestBrowser.NewName("Swim"), MondaysFromToday(new() { ["Weeks"] = routine.ToString() }));

        var form = WebUtility.HtmlDecode(await (await browser.GetAsync("/Activities/Create")).Content.ReadAsStringAsync());
        var list = WebUtility.HtmlDecode(await (await browser.GetAsync("/Activities")).Content.ReadAsStringAsync());

        Assert.Contains($"<optgroup label=\"{holiday} (not in use on any dates yet)\">", form);
        Assert.Contains($"Only while {holiday} is in use: it isn't on any dates yet, so this doesn't happen", list);
    }

    // The heading doesn't ask a question the page won't let you answer.
    [Fact]
    public async Task TheDeletePage_OfARoutineWithActivities_SaysItCantBeDeletedYet()
    {
        var (browser, routine) = await SignedInWithABRoutineAsync();
        await browser.AddActivityAsync(TestBrowser.NewName("Gym"), MondaysFromToday(new() { ["Weeks"] = $"{routine}/A" }));

        var html = WebUtility.HtmlDecode(await (await browser.GetAsync($"/Routines/Delete/{routine}")).Content.ReadAsStringAsync());

        Assert.Contains("This routine can't be deleted yet", html);
        Assert.DoesNotContain("Delete this routine?", html);
    }

    // P1: making another routine usual means the old one's activities stop happening (it's no longer in use). Said
    // once it's saved, naming how many.
    [Fact]
    public async Task MakingAnotherRoutineUsual_SaysTheOldOnesActivitiesStopHappening()
    {
        var (browser, routine, name) = await SignedInWithNamedABRoutineAsync();
        await browser.AddActivityAsync(TestBrowser.NewName("Gym"), MondaysFromToday(new() { ["Weeks"] = $"{routine}/A" }));

        var saved = await browser.AddRoutineAsync(TestBrowser.NewName("Busy week"), new() { ["IsUsual"] = "true" });
        var list = WebUtility.HtmlDecode(await (await browser.GetAsync("/Routines")).Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        Assert.Contains($"{name} is no longer your usual routine, so its 1 activity no longer happens", list);
    }

    [Fact]
    public async Task UntickingTheUsualRoutine_SaysItsActivitiesStopHappening()
    {
        var (browser, routine, name) = await SignedInWithNamedABRoutineAsync();
        await browser.AddActivityAsync(TestBrowser.NewName("Gym"), MondaysFromToday(new() { ["Weeks"] = $"{routine}/A" }));
        await browser.AddActivityAsync(TestBrowser.NewName("Run"), MondaysFromToday(new() { ["StartTime"] = "18:00", ["Weeks"] = $"{routine}/B" }));

        var saved = await browser.SubmitFormAsync($"/Routines/Edit/{routine}", $"/Routines/Edit/{routine}",
            TestBrowser.RoutineForm(name, new() { ["CycleWeeks"] = "2", ["IsUsual"] = "false" }));
        var list = WebUtility.HtmlDecode(await (await browser.GetAsync("/Routines")).Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        Assert.Contains($"{name} is no longer your usual routine, so its 2 activities no longer happen", list);
    }

    // Deleting a routine would otherwise leave its activities never happening, or quietly happening every week.
    [Fact]
    public async Task DeletingARoutineWithActivities_IsRefused_NamingThem()
    {
        var (browser, routine) = await SignedInWithABRoutineAsync();
        var title = TestBrowser.NewName("Gym");
        await browser.AddActivityAsync(title, MondaysFromToday(new() { ["Weeks"] = $"{routine}/A" }));

        var response = await browser.SubmitFormAsync($"/Routines/Delete/{routine}", $"/Routines/Delete/{routine}", []);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("1 activity is in this routine", html);
        Assert.Contains(title, html);
        Assert.NotNull(await StoredData.VersionOfAsync(factory, routine));
    }

    // Week B of a routine that repeats every week would never come round.
    [Fact]
    public async Task ShorteningACycle_PastAnActivitysWeek_IsRefused_NamingIt()
    {
        var (browser, routine, name) = await SignedInWithNamedABRoutineAsync();
        var title = TestBrowser.NewName("Gym");
        await browser.AddActivityAsync(title, MondaysFromToday(new() { ["Weeks"] = $"{routine}/B" }));

        var response = await browser.SubmitFormAsync($"/Routines/Edit/{routine}", $"/Routines/Edit/{routine}",
            TestBrowser.RoutineForm(name, new() { ["CycleWeeks"] = "1", ["IsUsual"] = "true" }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("would never happen", html);
        Assert.Contains(title, html);
        Assert.Equal(2, (await StoredData.RoutineNamedAsync(factory, name))!.CycleWeeks);
    }

    private async Task<(TestBrowser Browser, Guid Routine)> SignedInWithABRoutineAsync()
    {
        var (browser, routine, _) = await SignedInWithNamedABRoutineAsync();
        return (browser, routine);
    }

    // A two-week usual routine with week A this week (19 October 2026).
    private async Task<(TestBrowser Browser, Guid Routine, string Name)> SignedInWithNamedABRoutineAsync()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var name = TestBrowser.NewName("Usual week");
        var added = await browser.AddRoutineAsync(name, new() { ["CycleWeeks"] = "2", ["WeekA"] = "2026-10-19", ["IsUsual"] = "true" });
        Assert.Equal(HttpStatusCode.Redirect, added.StatusCode);
        return (browser, (await StoredData.RoutineNamedAsync(factory, name))!.Id, name);
    }

    // Weekly on Mondays at 07:00 for an hour, from today (Monday 19 October 2026), with the given changes.
    private static Dictionary<string, string> MondaysFromToday(Dictionary<string, string>? changes = null)
    {
        var form = new Dictionary<string, string>
        {
            ["StartDate"] = "2026-10-19",
            ["Repeats"] = "Weekly",
            ["RepeatDays[0]"] = "true",
        };
        foreach (var (field, value) in changes ?? [])
        {
            form[field] = value;
        }

        return form;
    }

    private static async Task<List<JsonElement>> EventsAsync(TestBrowser browser, string start, string end)
    {
        var response = await browser.GetAsync($"/Calendar/Events?start={start}&end={end}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.EnumerateArray().Select(item => item.Clone()).ToList();
    }
}
