using System.Net;
using System.Text.Json;
using FitnessTracker.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FitnessTracker.Web.Tests.Routines;

// CAL-9, Q14: a routine chosen for a range of dates (a holiday week) is in use on them instead of the usual one, so its
// activities happen then and the usual week's don't. Today in tests is Monday 19 October 2026. Each test's user has a
// usual routine and a holiday routine, both the same every week; activities are weekly on Mondays at 07:00.
public sealed class RoutineDatesTests(TestAppFactory factory) : IClassFixture<TestAppFactory>
{
    [Fact]
    public async Task ChoosingARoutineForSomeDates_PutsItsActivitiesThere_InsteadOfTheUsualOnes()
    {
        var (browser, usual, holiday) = await SignedInWithUsualAndHolidayAsync();
        var office = await AddMondaysAsync(browser, "Office", usual);
        var beach = await AddMondaysAsync(browser, "Beach run", holiday);

        var response = await browser.ChooseRoutineDatesAsync(holiday.Id, "2026-11-02", "2026-11-08");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Routines", response.Headers.Location?.OriginalString);
        var before = await TitlesAsync(browser, "2026-10-26", "2026-11-02");
        var during = await TitlesAsync(browser, "2026-11-02", "2026-11-09");
        var after = await TitlesAsync(browser, "2026-11-09", "2026-11-16");
        Assert.Contains(office, before);
        Assert.DoesNotContain(beach, before);
        Assert.Contains(beach, during);
        Assert.DoesNotContain(office, during);
        Assert.Contains(office, after);
        var stored = Assert.Single(await StoredData.DatesChosenForAsync(factory, holiday.Id));
        Assert.Equal((new NodaTime.LocalDate(2026, 11, 2), new NodaTime.LocalDate(2026, 11, 8)), (stored.StartDate, stored.EndDate));
    }

    // The calendar and Plan my week say which routine those days are in.
    [Fact]
    public async Task TheDatesChosen_AreNamedOnTheCalendarAndPlanMyWeek()
    {
        var (browser, usual, holiday) = await SignedInWithUsualAndHolidayAsync();
        await browser.ChooseRoutineDatesAsync(holiday.Id, "2026-11-07", "2026-11-08");

        var weeks = await WeeksTextAsync(browser, "2026-11-02", "2026-11-09");
        var plan = WebUtility.HtmlDecode(await (await browser.GetAsync("/Plan?date=2026-11-02")).Content.ReadAsStringAsync());

        Assert.Equal($"Mon 2 Nov – Fri 6 Nov: {usual.Name}; Sat 7 Nov – Sun 8 Nov: {holiday.Name}", weeks);
        Assert.Contains($"Mon 2 Nov – Fri 6 Nov: {usual.Name}; Sat 7 Nov – Sun 8 Nov: {holiday.Name}", plan);
    }

    // The Routines page lists them, earliest first, each with buttons to change or remove it.
    [Fact]
    public async Task TheRoutinesPage_ListsTheDatesChosen()
    {
        var (browser, _, holiday) = await SignedInWithUsualAndHolidayAsync();
        await browser.ChooseRoutineDatesAsync(holiday.Id, "2026-12-21", "2026-12-27");
        await browser.ChooseRoutineDatesAsync(holiday.Id, "2026-11-02", "2026-11-08");
        var chosen = await StoredData.DatesChosenForAsync(factory, holiday.Id);

        var html = WebUtility.HtmlDecode(await (await browser.GetAsync("/Routines")).Content.ReadAsStringAsync());

        var november = html.IndexOf($"Mon 2 Nov 2026 – Sun 8 Nov 2026: <span dir=\"auto\">{holiday.Name}</span>", StringComparison.Ordinal);
        var december = html.IndexOf($"Mon 21 Dec 2026 – Sun 27 Dec 2026: <span dir=\"auto\">{holiday.Name}</span>", StringComparison.Ordinal);
        Assert.True(november >= 0 && december > november);
        Assert.Contains($"href=\"/RoutineDates/Edit/{chosen[0].Id}\"", html);
        Assert.Contains($"href=\"/RoutineDates/Delete/{chosen[0].Id}\"", html);
    }

    [Theory]
    [InlineData("", "2026-11-08", "Enter the first date.")]
    [InlineData("2026-11-02", "", "Enter the last date.")]
    [InlineData("2026-11-31", "2026-12-08", "Enter a real first date, for example 26 December 2026.")]
    [InlineData("2026-11-02", "08/11/2026", "Enter a real last date, for example 3 January 2027.")]
    [InlineData("2026-11-08", "2026-11-02", "The last date can't be before the first.")]
    public async Task Choosing_RefusesDatesItCantUse(string from, string to, string message)
    {
        var (browser, _, holiday) = await SignedInWithUsualAndHolidayAsync();

        var response = await browser.ChooseRoutineDatesAsync(holiday.Id, from, to);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(message, WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        Assert.Empty(await StoredData.DatesChosenForAsync(factory, holiday.Id));
    }

    // Two routines can't both be in use on a date, so ranges that overlap are refused, naming the one in the way.
    // Ranges that only touch are fine.
    [Fact]
    public async Task Choosing_RefusesDatesThatOverlapOthersChosen()
    {
        var (browser, _, holiday) = await SignedInWithUsualAndHolidayAsync();
        await browser.ChooseRoutineDatesAsync(holiday.Id, "2026-11-02", "2026-11-08");

        var overlapping = await browser.ChooseRoutineDatesAsync(holiday.Id, "2026-11-07", "2026-11-10");
        var touching = await browser.ChooseRoutineDatesAsync(holiday.Id, "2026-11-09", "2026-11-10");

        Assert.Equal(HttpStatusCode.OK, overlapping.StatusCode);
        Assert.Contains($"These dates overlap those chosen for {holiday.Name}, Mon 2 Nov 2026 – Sun 8 Nov 2026. Change those first, or choose other dates.",
            WebUtility.HtmlDecode(await overlapping.Content.ReadAsStringAsync()));
        Assert.Equal(HttpStatusCode.Redirect, touching.StatusCode);
        Assert.Equal(2, (await StoredData.DatesChosenForAsync(factory, holiday.Id)).Count);
    }

    // CLAUDE.md rule 5: a routine must be the user's own; another user's is "not found" by the filter, and reported
    // like a choice that isn't on the list.
    [Fact]
    public async Task Choosing_RefusesNoRoutine_AndAnotherUsersRoutine()
    {
        var (_, _, alexsHoliday) = await SignedInWithUsualAndHolidayAsync();
        var (sam, _, _) = await SignedInWithUsualAndHolidayAsync("sam");

        var none = await sam.SubmitFormAsync("/RoutineDates/Create", "/RoutineDates/Create",
            new(TestBrowser.RoutineDatesForm(Guid.Empty, "2026-11-02", "2026-11-08")) { ["RoutineId"] = "" });
        var alexs = await sam.ChooseRoutineDatesAsync(alexsHoliday.Id, "2026-11-02", "2026-11-08");

        Assert.Contains("Choose a routine for these dates.", await none.Content.ReadAsStringAsync());
        Assert.Contains("Choose a routine from the list.", await alexs.Content.ReadAsStringAsync());
        Assert.Empty(await StoredData.DatesChosenForAsync(factory, alexsHoliday.Id));
    }

    // The add page can start with a routine chosen (from that routine's own button).
    [Fact]
    public async Task TheAddPage_CanStartWithARoutineChosen()
    {
        var (browser, _, holiday) = await SignedInWithUsualAndHolidayAsync();

        var html = await (await browser.GetAsync($"/RoutineDates/Create?routine={holiday.Id}")).Content.ReadAsStringAsync();

        Assert.Contains($"<option selected=\"selected\" value=\"{holiday.Id}\">", html);
    }

    // Changing the dates; its own old dates aren't in the way.
    [Fact]
    public async Task Change_MovesTheDates()
    {
        var (browser, _, holiday) = await SignedInWithUsualAndHolidayAsync();
        await browser.ChooseRoutineDatesAsync(holiday.Id, "2026-11-02", "2026-11-08");
        var id = Assert.Single(await StoredData.DatesChosenForAsync(factory, holiday.Id)).Id;

        var response = await browser.SubmitFormAsync($"/RoutineDates/Edit/{id}", $"/RoutineDates/Edit/{id}",
            TestBrowser.RoutineDatesForm(holiday.Id, "2026-11-04", "2026-11-12"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var stored = Assert.Single(await StoredData.DatesChosenForAsync(factory, holiday.Id));
        Assert.Equal((new NodaTime.LocalDate(2026, 11, 4), new NodaTime.LocalDate(2026, 11, 12)), (stored.StartDate, stored.EndDate));
    }

    // Requirements §9: a change made from a page opened before another tab changed the dates isn't saved over it.
    [Fact]
    public async Task Change_FromAnOutOfDatePage_IsNotSaved_AndSaysSo()
    {
        var (browser, _, holiday) = await SignedInWithUsualAndHolidayAsync();
        await browser.ChooseRoutineDatesAsync(holiday.Id, "2026-11-02", "2026-11-08");
        var id = Assert.Single(await StoredData.DatesChosenForAsync(factory, holiday.Id)).Id;
        var oldPage = await browser.LoadFormAsync($"/RoutineDates/Edit/{id}");
        await browser.SubmitFormAsync($"/RoutineDates/Edit/{id}", $"/RoutineDates/Edit/{id}", TestBrowser.RoutineDatesForm(holiday.Id, "2026-11-03", "2026-11-08"));

        var response = await browser.SubmitAsync(oldPage, $"/RoutineDates/Edit/{id}", TestBrowser.RoutineDatesForm(holiday.Id, "2026-11-05", "2026-11-08"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("These dates were changed in another tab or window after you opened this page.",
            WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        Assert.Equal(new NodaTime.LocalDate(2026, 11, 3), Assert.Single(await StoredData.DatesChosenForAsync(factory, holiday.Id)).StartDate);
    }

    // Removing them asks first; then those dates go back to the usual routine.
    [Fact]
    public async Task Remove_AsksFirst_ThenTheUsualRoutineIsBack()
    {
        var (browser, _, holiday) = await SignedInWithUsualAndHolidayAsync();
        await browser.ChooseRoutineDatesAsync(holiday.Id, "2026-11-02", "2026-11-08");
        var id = Assert.Single(await StoredData.DatesChosenForAsync(factory, holiday.Id)).Id;

        var page = WebUtility.HtmlDecode(await (await browser.GetAsync($"/RoutineDates/Delete/{id}")).Content.ReadAsStringAsync());
        var response = await browser.SubmitFormAsync($"/RoutineDates/Delete/{id}", $"/RoutineDates/Delete/{id}", []);

        Assert.Contains($"Mon 2 Nov 2026 – Sun 8 Nov 2026: <span dir=\"auto\">{holiday.Name}</span>", page);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Empty(await StoredData.DatesChosenForAsync(factory, holiday.Id));
    }

    // Deleting a routine says that the dates chosen for it go back to the usual routine, and removes them with it.
    [Fact]
    public async Task DeletingARoutine_SaysItsDatesGoBack_AndRemovesThem()
    {
        var (browser, _, holiday) = await SignedInWithUsualAndHolidayAsync();
        await browser.ChooseRoutineDatesAsync(holiday.Id, "2026-11-02", "2026-11-08");

        var page = WebUtility.HtmlDecode(await (await browser.GetAsync($"/Routines/Delete/{holiday.Id}")).Content.ReadAsStringAsync());
        var response = await browser.SubmitFormAsync($"/Routines/Delete/{holiday.Id}", $"/Routines/Delete/{holiday.Id}", []);

        Assert.Contains("It is chosen for Mon 2 Nov 2026 – Sun 8 Nov 2026. Those dates go back to your usual routine.", page);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Null(await StoredData.RoutineNamedAsync(factory, holiday.Name));
        Assert.Empty(await StoredData.DatesChosenForAsync(factory, holiday.Id));
    }

    // SCH-1, CLAUDE.md rule 3: choosing a routine for some dates puts its activities on them, so they go through the
    // check on save, and what that breaks is shown (as for a timezone move). Work every Monday at 07:00, whatever the
    // routine, and a holiday beach run then too: they clash once the holiday is chosen.
    [Fact]
    public async Task Choosing_ShowsWhatItBreaks()
    {
        var (browser, _, holiday) = await SignedInWithUsualAndHolidayAsync();
        var work = await AddMondaysAsync(browser, "Work", routine: null);
        var beach = await AddMondaysAsync(browser, "Beach run", holiday);

        await browser.ChooseRoutineDatesAsync(holiday.Id, "2026-11-02", "2026-11-08");

        var html = WebUtility.HtmlDecode(await (await browser.GetAsync("/Routines")).Content.ReadAsStringAsync());
        Assert.Contains("The activities on the dates that changed were checked. Some now need a look:", html);
        Assert.Contains($"{beach}: On Mon 2 Nov 2026", html);
        Assert.Contains(work, html);
    }

    // ... and so does making another routine the usual one, which puts its activities on every date not chosen for
    // another (the known limitation "doesn't re-check the clashes that creates").
    [Fact]
    public async Task MakingAnotherRoutineUsual_ShowsWhatItBreaks()
    {
        var (browser, _, holiday) = await SignedInWithUsualAndHolidayAsync();
        var work = await AddMondaysAsync(browser, "Work", routine: null);
        var beach = await AddMondaysAsync(browser, "Beach run", holiday);

        await browser.SubmitFormAsync($"/Routines/Edit/{holiday.Id}", $"/Routines/Edit/{holiday.Id}",
            TestBrowser.RoutineForm(holiday.Name, new() { ["IsUsual"] = "true" }));

        var html = WebUtility.HtmlDecode(await (await browser.GetAsync("/Routines")).Content.ReadAsStringAsync());
        Assert.Contains("Some now need a look:", html);
        Assert.Contains($"{beach}: On Mon 19 Oct 2026", html);
        Assert.Contains(work, html);
    }

    // ... but at most 20, counting the rest: the list travels in a cookie to the next page, and hundreds would be too
    // big for it (code review, Phase 6b). Work and a holiday run every day at 07:00; the holiday is checked from its
    // first day, 2 November, for 8 weeks, to 27 December: 56 clashes.
    [Fact]
    public async Task Choosing_ShowsAtMost20OfWhatItBreaks_AndCountsTheRest()
    {
        var (browser, _, holiday) = await SignedInWithUsualAndHolidayAsync();
        await browser.AddActivityAsync(TestBrowser.NewName("Work"), new() { ["StartDate"] = "2026-10-19", ["Repeats"] = "Daily" });
        var beach = TestBrowser.NewName("Beach run");
        await browser.AddActivityAsync(beach, new() { ["StartDate"] = "2026-10-19", ["Repeats"] = "Daily", ["Weeks"] = holiday.Id.ToString() });

        await browser.ChooseRoutineDatesAsync(holiday.Id, "2026-11-02", "2026-12-31");

        var html = WebUtility.HtmlDecode(await (await browser.GetAsync("/Routines")).Content.ReadAsStringAsync());
        Assert.Equal(20, html.Split($"<li>{beach}: ").Length - 1);
        Assert.Contains("And 36 more.", html);
    }

    // ... also for dates more than 8 weeks ahead (21 December is 9 weeks after today), which are checked from their first
    // day (scheduling review, Phase 6b: only the 8 weeks from today were checked, where the holiday has nothing).
    [Fact]
    public async Task Choosing_DatesMoreThanEightWeeksAhead_ShowsWhatItBreaksThere()
    {
        var (browser, _, holiday) = await SignedInWithUsualAndHolidayAsync();
        var work = await AddMondaysAsync(browser, "Work", routine: null);
        var beach = await AddMondaysAsync(browser, "Beach run", holiday);

        await browser.ChooseRoutineDatesAsync(holiday.Id, "2026-12-21", "2026-12-27");

        var html = WebUtility.HtmlDecode(await (await browser.GetAsync("/Routines")).Content.ReadAsStringAsync());
        Assert.Contains($"{beach}: On Mon 21 Dec 2026", html);
        Assert.Contains(work, html);
    }

    // ... and for a second holiday after one that is soon (2-8 November, then 21-27 December): the holiday activity
    // happens in the 8 weeks from today, so its own check stops on 13 December, but choosing December's dates checks
    // them from their first day.
    [Fact]
    public async Task Choosing_ASecondHolidayMonthsAhead_ShowsWhatItBreaksThere()
    {
        var (browser, _, holiday) = await SignedInWithUsualAndHolidayAsync();
        await AddMondaysAsync(browser, "Work", routine: null);
        var beach = await AddMondaysAsync(browser, "Beach run", holiday);
        await browser.ChooseRoutineDatesAsync(holiday.Id, "2026-11-02", "2026-11-08");
        await browser.GetAsync("/Routines");

        await browser.ChooseRoutineDatesAsync(holiday.Id, "2026-12-21", "2026-12-27");

        var html = WebUtility.HtmlDecode(await (await browser.GetAsync("/Routines")).Content.ReadAsStringAsync());
        Assert.Contains($"{beach}: On Mon 21 Dec 2026", html);
        Assert.DoesNotContain($"{beach}: On Mon 2 Nov 2026", html);
    }

    // ... and an activity added to that routine afterwards is checked there when it is saved.
    [Fact]
    public async Task AddingAnActivityToARoutineChosenMonthsAhead_IsCheckedThere()
    {
        var (browser, _, holiday) = await SignedInWithUsualAndHolidayAsync();
        await AddMondaysAsync(browser, "Work", routine: null);
        await browser.ChooseRoutineDatesAsync(holiday.Id, "2026-12-21", "2026-12-27");

        var response = await browser.AddActivityAsync(TestBrowser.NewName("Beach run"), new()
        {
            ["StartDate"] = "2026-10-19",
            ["Repeats"] = "Weekly",
            ["RepeatDays[0]"] = "true",
            ["Weeks"] = holiday.Id.ToString(),
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("It clashes with your other activities", html);
        Assert.Contains("On Mon 21 Dec 2026", html);
    }

    // ... and says nothing when nothing breaks.
    [Fact]
    public async Task Choosing_SaysNothingMore_WhenNothingBreaks()
    {
        var (browser, _, holiday) = await SignedInWithUsualAndHolidayAsync();
        await AddMondaysAsync(browser, "Beach run", holiday);

        await browser.ChooseRoutineDatesAsync(holiday.Id, "2026-11-02", "2026-11-08");

        var html = await (await browser.GetAsync("/Routines")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("need a look", html);
    }

    // CAL-4 with Q14: a one-off change (Monday 2 November cancelled) in a week later given to a holiday isn't removed by
    // editing the activity, which didn't take that day away, and is back when the holiday is removed (the known
    // limitation "a change made in a week later given to a holiday routine would be removed by any edit").
    [Fact]
    public async Task EditingAnActivity_KeepsAChangeInAWeekGivenToAHoliday()
    {
        var (browser, usual, holiday) = await SignedInWithUsualAndHolidayAsync();
        var office = await AddMondaysAsync(browser, "Office", usual);
        var id = (await StoredData.ActivityTitledAsync(factory, office))!.Id;
        var cancelled = await browser.SubmitFormAsync($"/Activities/Occurrence/{id}?date=2026-11-02", $"/Activities/Occurrence/{id}",
            new() { ["Date"] = "2026-11-02", ["Change"] = "Cancel" });
        Assert.Equal(HttpStatusCode.Redirect, cancelled.StatusCode);
        await browser.ChooseRoutineDatesAsync(holiday.Id, "2026-11-02", "2026-11-08");

        var edited = await browser.SubmitFormAsync($"/Activities/Edit/{id}", $"/Activities/Edit/{id}", TestBrowser.ActivityForm(office, new()
        {
            ["StartDate"] = "2026-10-19",
            ["StartTime"] = "07:30",
            ["Repeats"] = "Weekly",
            ["RepeatDays[0]"] = "true",
            ["Weeks"] = usual.Id.ToString(),
        }));
        var list = await (await browser.GetAsync("/Activities")).Content.ReadAsStringAsync();
        var dates = Assert.Single(await StoredData.DatesChosenForAsync(factory, holiday.Id)).Id;
        await browser.SubmitFormAsync($"/RoutineDates/Delete/{dates}", $"/RoutineDates/Delete/{dates}", []);

        Assert.Equal(HttpStatusCode.Redirect, edited.StatusCode);
        Assert.DoesNotContain("removed", list);
        Assert.DoesNotContain(office, await TitlesAsync(browser, "2026-11-02", "2026-11-03"));
        Assert.Contains(office, await TitlesAsync(browser, "2026-11-09", "2026-11-10"));
    }

    // P1: dates the app can't use (edited by hand so the last is before the first) are listed, saying so, so they can be
    // removed, and don't stop other dates being chosen (code review, Phase 6b: they were hidden but still in the way).
    [Fact]
    public async Task DatesThatCantBeUsed_AreListed_AndNotInTheWay()
    {
        var (browser, _, holiday) = await SignedInWithUsualAndHolidayAsync();
        await browser.ChooseRoutineDatesAsync(holiday.Id, "2026-11-02", "2026-11-08");
        var id = Assert.Single(await StoredData.DatesChosenForAsync(factory, holiday.Id)).Id;
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<FitnessTracker.Web.Data.AppDbContext>().RoutineDateRanges.IgnoreQueryFilters()
                .Where(dates => dates.Id == id)
                .ExecuteUpdateAsync(set => set.SetProperty(dates => dates.StartDate, new NodaTime.LocalDate(2026, 11, 9)));
        }

        var html = WebUtility.HtmlDecode(await (await browser.GetAsync("/Routines")).Content.ReadAsStringAsync());
        var chosen = await browser.ChooseRoutineDatesAsync(holiday.Id, "2026-11-03", "2026-11-05");

        Assert.Contains("Mon 9 Nov 2026 – Sun 8 Nov 2026: ", html);
        Assert.Contains("These dates can't be used: the last is before the first.", html);
        Assert.Contains($"href=\"/RoutineDates/Delete/{id}\"", html);
        Assert.Equal(HttpStatusCode.Redirect, chosen.StatusCode);
    }

    // The activities list says when an activity in a routine other than the usual one happens.
    [Fact]
    public async Task TheActivitiesList_SaysWhichDatesAnActivityInAnotherRoutineHappensOn()
    {
        var (browser, _, holiday) = await SignedInWithUsualAndHolidayAsync();
        var beach = await AddMondaysAsync(browser, "Beach run", holiday);
        await browser.ChooseRoutineDatesAsync(holiday.Id, "2026-11-02", "2026-11-08");
        await browser.ChooseRoutineDatesAsync(holiday.Id, "2026-12-21", "2026-12-27");

        var list = WebUtility.HtmlDecode(await (await browser.GetAsync("/Activities")).Content.ReadAsStringAsync());

        Assert.Contains($"Only while {holiday.Name} is in use: Mon 2 Nov 2026 – Sun 8 Nov 2026, and 1 more", list);
        Assert.Contains(beach, list);
    }

    // A usual routine and a holiday routine, each the same every week, with week A this week.
    private async Task<(TestBrowser Browser, (Guid Id, string Name) Usual, (Guid Id, string Name) Holiday)> SignedInWithUsualAndHolidayAsync(string user = "alex")
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, user);
        var usual = TestBrowser.NewName("Usual week");
        var holiday = TestBrowser.NewName("Holiday");
        Assert.Equal(HttpStatusCode.Redirect, (await browser.AddRoutineAsync(usual, new() { ["IsUsual"] = "true" })).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await browser.AddRoutineAsync(holiday)).StatusCode);
        return (browser,
            ((await StoredData.RoutineNamedAsync(factory, usual))!.Id, usual),
            ((await StoredData.RoutineNamedAsync(factory, holiday))!.Id, holiday));
    }

    // Weekly on Mondays at 07:00 for an hour from today, in every week of the routine (or every week, with none).
    private async Task<string> AddMondaysAsync(TestBrowser browser, string label, (Guid Id, string Name)? routine)
    {
        var title = TestBrowser.NewName(label);
        var added = await browser.AddActivityAsync(title, new()
        {
            ["StartDate"] = "2026-10-19",
            ["Repeats"] = "Weekly",
            ["RepeatDays[0]"] = "true",
            ["Weeks"] = routine?.Id.ToString() ?? "",
        });
        Assert.Equal(HttpStatusCode.Redirect, added.StatusCode);
        return title;
    }

    private static async Task<List<string>> TitlesAsync(TestBrowser browser, string start, string end)
    {
        var response = await browser.GetAsync($"/Calendar/Events?start={start}&end={end}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.EnumerateArray()
            .Where(item => item.TryGetProperty("title", out _))
            .Select(item => item.GetProperty("title").GetString()!)
            .ToList();
    }

    private static async Task<string?> WeeksTextAsync(TestBrowser browser, string start, string end)
    {
        var response = await browser.GetAsync($"/Calendar/Weeks?start={start}&end={end}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var text = json.RootElement.GetProperty("text");
        return text.ValueKind == JsonValueKind.Null ? null : text.GetString();
    }
}
