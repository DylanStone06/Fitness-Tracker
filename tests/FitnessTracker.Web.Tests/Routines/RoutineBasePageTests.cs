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

    // A duplicate keeps the base (Q16: the same weeks, and now the same base).
    [Fact]
    public async Task ADuplicate_KeepsTheBase()
    {
        var setup = await SignedInWithABrightonWeekendAsync();
        var copy = TestBrowser.NewName("Brighton copy");

        await setup.Browser.SubmitFormAsync($"/Routines/Duplicate/{setup.Brighton.Id}", $"/Routines/Duplicate/{setup.Brighton.Id}", new() { ["Name"] = copy });

        Assert.Equal(setup.Flat.Id, (await StoredData.RoutineNamedAsync(factory, copy))!.BaseLocationId);
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

    private async Task<(Guid Id, string Name)> AddLocationAsync(TestBrowser browser, string label, bool isHome)
    {
        var name = TestBrowser.NewName(label);
        Assert.Equal(HttpStatusCode.Redirect, (await browser.AddLocationAsync(name, new() { ["IsHome"] = isHome ? "true" : "false" })).StatusCode);
        return ((await StoredData.LocationNamedAsync(factory, name))!.Id, name);
    }
}
