using System.Net;
using FitnessTracker.Web.Tests.Infrastructure;

namespace FitnessTracker.Web.Tests.Routines;

// TRV-4, T15: a routine can have its own base ("Brighton weekend": the Brighton flat), and on the dates it is in use
// travel starts and ends there. Today in tests is Monday 19 October 2026. Each test's user has a home, the town gym
// (25 minutes from home each way) and a Brighton flat with no travel times, so the gym can't be reached from it; the
// Brighton weekend routine, based at the flat, is chosen for Saturday 7 and Sunday 8 November.
public sealed class RoutineBasePageTests(TestAppFactory factory) : IClassFixture<TestAppFactory>
{
    [Fact]
    public async Task ARoutine_CanHaveABase_AndTheListSaysSo()
    {
        var setup = await SignedInWithABrightonWeekendAsync();

        var stored = await StoredData.RoutineNamedAsync(factory, setup.Brighton.Name);
        var list = WebUtility.HtmlDecode(await (await setup.Browser.GetAsync("/Routines")).Content.ReadAsStringAsync());

        Assert.Equal(setup.Flat.Id, stored!.BaseLocationId);
        Assert.Contains($"Based at <span dir=\"auto\">{setup.Flat.Name}</span>", list);
    }

    // The form offers the other locations as a base, after "Home, as usual"; the home location would mean the same, so
    // it isn't offered twice (break-ui, Phase 6b: the list said "Home, as usual" and then "Home").
    [Fact]
    public async Task TheForm_OffersEveryLocationButHome()
    {
        var setup = await SignedInWithABrightonWeekendAsync();

        var html = await (await setup.Browser.GetAsync("/Routines/Create")).Content.ReadAsStringAsync();

        Assert.Contains("<option value=\"\">Home, as usual</option>", html);
        Assert.Contains($"value=\"{setup.Flat.Id}\"", html);
        Assert.Contains($"value=\"{setup.Gym.Id}\"", html);
        Assert.DoesNotContain($"value=\"{setup.Home.Id}\"", html);
    }

    // CLAUDE.md rule 5: the base must be one of the user's own locations; another user's is "not found" by the filter.
    [Fact]
    public async Task ARoutine_RefusesAnotherUsersLocationAsItsBase()
    {
        var alex = await SignedInWithABrightonWeekendAsync();
        var sam = await TestBrowser.OpenSignedInAsync(factory, "sam");
        var name = TestBrowser.NewName("Away");

        var response = await sam.AddRoutineAsync(name, new() { ["BaseLocationId"] = alex.Flat.Id.ToString() });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Choose a base from the list.", await response.Content.ReadAsStringAsync());
        Assert.Null(await StoredData.RoutineNamedAsync(factory, name));
    }

    // T15 on Plan my week: a gym session a day goes on Monday to Friday, the weekend saying why, and runs (no
    // particular place) are still suggested every day.
    [Fact]
    public async Task T15_PlanMyWeek_KeepsTheGymForWeekdays_AndStillSuggestsRuns()
    {
        var setup = await SignedInWithABrightonWeekendAsync();
        var gym = TestBrowser.NewName("Gym");
        var run = TestBrowser.NewName("Run");
        await setup.Browser.AddGoalAsync(gym, new() { ["SessionsPerWeek"] = "7", ["LocationId"] = setup.Gym.Id.ToString() });
        await setup.Browser.AddGoalAsync(run, new() { ["SessionsPerWeek"] = "7", ["Category"] = "Run" });

        var html = WebUtility.HtmlDecode(await (await setup.Browser.GetAsync("/Plan?date=2026-11-02")).Content.ReadAsStringAsync());

        Assert.Contains($"Sat 7 Nov: Your base then is {setup.Flat.Name}, and there's no travel time from {setup.Flat.Name} to {setup.Gym.Name}", html);
        Assert.Contains($"Sun 8 Nov: Your base then is {setup.Flat.Name}", html);
        Assert.Equal(5, html.Split($"<span class=\"fw-semibold text-break\" dir=\"auto\">{gym}</span>").Length - 1);
        Assert.Equal(7, html.Split($"<span class=\"fw-semibold text-break\" dir=\"auto\">{run}</span>").Length - 1);
    }

    // The times suggested for the Brighton weekend say they leave from the flat, the weekdays from home (scheduling
    // review, Phase 6b: every trip said "Leave home"). Here the gym is an hour from the flat.
    [Fact]
    public async Task PlanMyWeek_SaysTheBrightonSessionsLeaveFromTheFlat()
    {
        var setup = await SignedInWithABrightonWeekendAsync();
        Assert.Equal(HttpStatusCode.Redirect, (await setup.Browser.AddTravelLegAsync(setup.Flat.Id, setup.Gym.Id, 60, alsoTheReturnJourney: true)).StatusCode);
        await setup.Browser.AddGoalAsync(TestBrowser.NewName("Gym"), new() { ["SessionsPerWeek"] = "7", ["LocationId"] = setup.Gym.Id.ToString() });

        var html = WebUtility.HtmlDecode(await (await setup.Browser.GetAsync("/Plan?date=2026-11-02")).Content.ReadAsStringAsync());

        Assert.Contains($"Leave {setup.Flat.Name} ", html);
        Assert.Contains("Leave home ", html);
    }

    // ... and so does Find another time (SCH-6), for a gym session on the Brighton Saturday at 09:00. Work fills the
    // weekdays from 07:00 to 23:00, so every other time offered is at the weekend, from the flat.
    [Fact]
    public async Task FindAnotherTime_SaysTheBrightonTimesLeaveFromTheFlat()
    {
        var setup = await SignedInWithABrightonWeekendAsync();
        Assert.Equal(HttpStatusCode.Redirect, (await setup.Browser.AddTravelLegAsync(setup.Flat.Id, setup.Gym.Id, 60, alsoTheReturnJourney: true)).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await setup.Browser.AddActivityAsync(TestBrowser.NewName("Work"), new()
        {
            ["Category"] = "Work",
            ["StartDate"] = "2026-11-02",
            ["DurationHours"] = "16",
            ["Repeats"] = "Weekly",
            ["RepeatDays[0]"] = "true",
            ["RepeatDays[1]"] = "true",
            ["RepeatDays[2]"] = "true",
            ["RepeatDays[3]"] = "true",
            ["RepeatDays[4]"] = "true",
        })).StatusCode);
        var title = TestBrowser.NewName("Gym");
        Assert.Equal(HttpStatusCode.Redirect, (await setup.Browser.AddActivityAsync(title, new()
        {
            ["StartDate"] = "2026-11-07",
            ["StartTime"] = "09:00",
            ["LocationId"] = setup.Gym.Id.ToString(),
        })).StatusCode);
        var id = (await StoredData.ActivityTitledAsync(factory, title))!.Id;

        var html = WebUtility.HtmlDecode(await (await setup.Browser.GetAsync($"/Activities/Reschedule/{id}?date=2026-11-07")).Content.ReadAsStringAsync());

        Assert.Contains($"Leave {setup.Flat.Name} ", html);
    }

    // The check on save works out travel from each day's base: a gym session every Saturday can't be saved while the
    // gym can't be reached from the Brighton weekend's flat (P1: never guessed as zero).
    [Fact]
    public async Task TheCheckOnSave_UsesEachDaysBase()
    {
        var setup = await SignedInWithABrightonWeekendAsync();
        var title = TestBrowser.NewName("Gym");

        var response = await setup.Browser.AddActivityAsync(title, new()
        {
            ["StartDate"] = "2026-10-24",
            ["Repeats"] = "Weekly",
            ["RepeatDays[5]"] = "true",
            ["LocationId"] = setup.Gym.Id.ToString(),
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"On Sat 7 Nov 2026 your base is {setup.Flat.Name}, and there's no travel time from {setup.Flat.Name} to {setup.Gym.Name}",
            WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        Assert.Null(await StoredData.ActivityTitledAsync(factory, title));
    }

    // ... and in the usual routine only, it doesn't happen on the Brighton weekend, so it can be saved.
    [Fact]
    public async Task AnActivityInTheUsualRoutine_IsntCheckedFromTheBrightonBase()
    {
        var setup = await SignedInWithABrightonWeekendAsync();
        var title = TestBrowser.NewName("Gym");

        var response = await setup.Browser.AddActivityAsync(title, new()
        {
            ["StartDate"] = "2026-10-24",
            ["Repeats"] = "Weekly",
            ["RepeatDays[5]"] = "true",
            ["LocationId"] = setup.Gym.Id.ToString(),
            ["Weeks"] = setup.Usual.Id.ToString(),
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    // SCH-1: choosing dates for a routine with a base moves the travel of activities in every week too, so what that
    // breaks is said (code review, Phase 6b: only activities in a routine were re-checked). A gym session every Saturday
    // from 14 November is saved while those days are at home; the Brighton weekend chosen for 21-22 November puts one
    // out of reach.
    [Fact]
    public async Task ChoosingDatesForARoutineWithABase_SaysWhatItBreaksInEveryWeek()
    {
        var setup = await SignedInWithABrightonWeekendAsync();
        var gym = await AddSaturdayGymAsync(setup);

        await setup.Browser.ChooseRoutineDatesAsync(setup.Brighton.Id, "2026-11-21", "2026-11-22");

        var html = WebUtility.HtmlDecode(await (await setup.Browser.GetAsync("/Routines")).Content.ReadAsStringAsync());
        Assert.Contains($"{gym}: On Sat 21 Nov 2026 your base is {setup.Flat.Name}, and there's no travel time", html);
    }

    // ... and so does giving a routine already chosen for some dates a base.
    [Fact]
    public async Task GivingARoutineABase_SaysWhatItBreaksInEveryWeek()
    {
        var setup = await SignedInWithABrightonWeekendAsync();
        var gym = await AddSaturdayGymAsync(setup);
        var away = TestBrowser.NewName("Weekend away");
        Assert.Equal(HttpStatusCode.Redirect, (await setup.Browser.AddRoutineAsync(away)).StatusCode);
        var awayId = (await StoredData.RoutineNamedAsync(factory, away))!.Id;
        Assert.Equal(HttpStatusCode.Redirect, (await setup.Browser.ChooseRoutineDatesAsync(awayId, "2026-11-21", "2026-11-22")).StatusCode);

        await setup.Browser.SubmitFormAsync($"/Routines/Edit/{awayId}", $"/Routines/Edit/{awayId}",
            TestBrowser.RoutineForm(away, new() { ["BaseLocationId"] = setup.Flat.Id.ToString() }));

        var html = WebUtility.HtmlDecode(await (await setup.Browser.GetAsync("/Routines")).Content.ReadAsStringAsync());
        Assert.Contains($"{gym}: On Sat 21 Nov 2026 your base is {setup.Flat.Name}, and there's no travel time", html);
    }

    // ... also on dates more than 8 weeks ahead, which are checked from their first day, as when they are chosen (code
    // review, Phase 6b: a base change was checked from today only). The gym is 2 hours from the flat: on Saturday 6 March
    // 2027, a 09:00 session from there runs into breakfast at 07:00; from home (25 minutes) it doesn't.
    [Fact]
    public async Task GivingARoutineABase_SaysWhatItBreaksMoreThanEightWeeksAhead()
    {
        var setup = await SignedInWithABrightonWeekendAsync();
        Assert.Equal(HttpStatusCode.Redirect, (await setup.Browser.AddTravelLegAsync(setup.Flat.Id, setup.Gym.Id, 120, alsoTheReturnJourney: true)).StatusCode);
        var gym = TestBrowser.NewName("Gym");
        Assert.Equal(HttpStatusCode.Redirect, (await setup.Browser.AddActivityAsync(gym, new()
        {
            ["StartDate"] = "2026-11-14",
            ["StartTime"] = "09:00",
            ["Repeats"] = "Weekly",
            ["RepeatDays[5]"] = "true",
            ["LocationId"] = setup.Gym.Id.ToString(),
        })).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await setup.Browser.AddActivityAsync(TestBrowser.NewName("Breakfast"), new()
        {
            ["Category"] = "Other",
            ["StartDate"] = "2026-11-14",
            ["Repeats"] = "Weekly",
            ["RepeatDays[5]"] = "true",
        })).StatusCode);
        var spring = TestBrowser.NewName("Spring weekend");
        Assert.Equal(HttpStatusCode.Redirect, (await setup.Browser.AddRoutineAsync(spring)).StatusCode);
        var springId = (await StoredData.RoutineNamedAsync(factory, spring))!.Id;
        Assert.Equal(HttpStatusCode.Redirect, (await setup.Browser.ChooseRoutineDatesAsync(springId, "2027-03-06", "2027-03-07")).StatusCode);

        await setup.Browser.SubmitFormAsync($"/Routines/Edit/{springId}", $"/Routines/Edit/{springId}",
            TestBrowser.RoutineForm(spring, new() { ["BaseLocationId"] = setup.Flat.Id.ToString() }));

        var html = WebUtility.HtmlDecode(await (await setup.Browser.GetAsync("/Routines")).Content.ReadAsStringAsync());
        Assert.Contains($"{gym}: On Sat 6 Mar 2027,", html);
    }

    // ... and so does deleting a usual routine with a base, with no dates chosen: every date goes back to home
    // (scheduling review, Phase 6b: only a routine with dates was re-checked). Term time is based at the halls flat, the
    // only place the gym has a travel time from.
    [Fact]
    public async Task DeletingAUsualRoutineWithABase_SaysWhatItBreaksInEveryWeek()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var home = await AddLocationAsync(browser, "Home", isHome: true);
        var gym = await AddLocationAsync(browser, "Town gym", isHome: false);
        var halls = await AddLocationAsync(browser, "Halls flat", isHome: false);
        Assert.Equal(HttpStatusCode.Redirect, (await browser.AddTravelLegAsync(halls.Id, gym.Id, 15, alsoTheReturnJourney: true)).StatusCode);
        var term = TestBrowser.NewName("Term time");
        Assert.Equal(HttpStatusCode.Redirect, (await browser.AddRoutineAsync(term, new() { ["IsUsual"] = "true", ["BaseLocationId"] = halls.Id.ToString() })).StatusCode);
        var title = TestBrowser.NewName("Gym");
        Assert.Equal(HttpStatusCode.Redirect, (await browser.AddActivityAsync(title, new()
        {
            ["StartDate"] = "2026-10-26",
            ["Repeats"] = "Weekly",
            ["RepeatDays[0]"] = "true",
            ["LocationId"] = gym.Id.ToString(),
        })).StatusCode);
        var termId = (await StoredData.RoutineNamedAsync(factory, term))!.Id;

        await browser.SubmitFormAsync($"/Routines/Delete/{termId}", $"/Routines/Delete/{termId}", []);

        var html = WebUtility.HtmlDecode(await (await browser.GetAsync("/Routines")).Content.ReadAsStringAsync());
        Assert.Null(await StoredData.RoutineNamedAsync(factory, term));
        Assert.Contains($"{title}: There's no travel time from {home.Name} to {gym.Name}", html);
    }

    // A duplicate keeps the base (Q16: the same weeks, and now the same base).
    [Fact]
    public async Task ADuplicate_KeepsTheBase()
    {
        var setup = await SignedInWithABrightonWeekendAsync();
        var copy = TestBrowser.NewName("Brighton copy");

        await setup.Browser.SubmitFormAsync($"/Routines/Duplicate/{setup.Brighton.Id}", $"/Routines/Duplicate/{setup.Brighton.Id}", new() { ["Name"] = copy });

        Assert.Equal(setup.Flat.Id, (await StoredData.RoutineNamedAsync(factory, copy))!.BaseLocationId);
    }

    // The delete page says so first, as it does for the travel times and activities that go with a location (code
    // review, Phase 6b: the routine moved back to home unannounced).
    [Fact]
    public async Task TheBasesDeletePage_NamesTheRoutinesBasedThere()
    {
        var setup = await SignedInWithABrightonWeekendAsync();

        var page = await (await setup.Browser.GetAsync($"/Locations/Delete/{setup.Flat.Id}")).Content.ReadAsStringAsync();
        var gymPage = await (await setup.Browser.GetAsync($"/Locations/Delete/{setup.Gym.Id}")).Content.ReadAsStringAsync();

        Assert.Contains($"Routines based here are based at home again: <span dir=\"auto\">{setup.Brighton.Name}</span>.", WebUtility.HtmlDecode(page));
        Assert.DoesNotContain("Routines based here", gymPage);
    }

    // Deleting the base location keeps the routine, based at home again.
    [Fact]
    public async Task DeletingTheBase_LeavesTheRoutineAtHome()
    {
        var setup = await SignedInWithABrightonWeekendAsync();

        var deleted = await setup.Browser.SubmitFormAsync($"/Locations/Delete/{setup.Flat.Id}", $"/Locations/Delete/{setup.Flat.Id}", []);

        Assert.Equal(HttpStatusCode.Redirect, deleted.StatusCode);
        Assert.Null((await StoredData.RoutineNamedAsync(factory, setup.Brighton.Name))!.BaseLocationId);
    }

    private sealed record Setup(TestBrowser Browser, (Guid Id, string Name) Home, (Guid Id, string Name) Gym, (Guid Id, string Name) Flat, (Guid Id, string Name) Usual, (Guid Id, string Name) Brighton);

    private async Task<Setup> SignedInWithABrightonWeekendAsync()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var home = await AddLocationAsync(browser, "Home", isHome: true);
        var gym = await AddLocationAsync(browser, "Town gym", isHome: false);
        var flat = await AddLocationAsync(browser, "Brighton flat", isHome: false);
        Assert.Equal(HttpStatusCode.Redirect, (await browser.AddTravelLegAsync(home.Id, gym.Id, 25, alsoTheReturnJourney: true)).StatusCode);

        var usual = TestBrowser.NewName("Usual week");
        Assert.Equal(HttpStatusCode.Redirect, (await browser.AddRoutineAsync(usual, new() { ["IsUsual"] = "true" })).StatusCode);
        var brighton = TestBrowser.NewName("Brighton weekend");
        Assert.Equal(HttpStatusCode.Redirect, (await browser.AddRoutineAsync(brighton, new() { ["BaseLocationId"] = flat.Id.ToString() })).StatusCode);
        var brightonId = (await StoredData.RoutineNamedAsync(factory, brighton))!.Id;
        Assert.Equal(HttpStatusCode.Redirect, (await browser.ChooseRoutineDatesAsync(brightonId, "2026-11-07", "2026-11-08")).StatusCode);
        return new Setup(browser, home, gym, flat, ((await StoredData.RoutineNamedAsync(factory, usual))!.Id, usual), (brightonId, brighton));
    }

    // A gym session every Saturday, in every week, from 14 November: after the Brighton weekend chosen in the setup.
    private static async Task<string> AddSaturdayGymAsync(Setup setup)
    {
        var title = TestBrowser.NewName("Gym");
        Assert.Equal(HttpStatusCode.Redirect, (await setup.Browser.AddActivityAsync(title, new()
        {
            ["StartDate"] = "2026-11-14",
            ["Repeats"] = "Weekly",
            ["RepeatDays[5]"] = "true",
            ["LocationId"] = setup.Gym.Id.ToString(),
        })).StatusCode);
        return title;
    }

    private async Task<(Guid Id, string Name)> AddLocationAsync(TestBrowser browser, string label, bool isHome)
    {
        var name = TestBrowser.NewName(label);
        Assert.Equal(HttpStatusCode.Redirect, (await browser.AddLocationAsync(name, new() { ["IsHome"] = isHome ? "true" : "false" })).StatusCode);
        return ((await StoredData.LocationNamedAsync(factory, name))!.Id, name);
    }
}
