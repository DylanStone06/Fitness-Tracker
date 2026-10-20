using System.Net;
using FitnessTracker.Web.Data;
using FitnessTracker.Web.Tests.Infrastructure;

namespace FitnessTracker.Web.Tests.Home;

// GOL-2, first part: the home page shows what is still to set up, and today and the next 7 days with when to leave and
// any clashes. "Today" is Monday 19 October 2026 (FixedClock); London is on BST until the 25th.
public sealed class HomeTests(TestAppFactory factory) : IClassFixture<TestAppFactory>
{
    [Fact]
    public async Task ANewUser_SeesWhatToSetUp_AndAnEmptyWeek()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");

        var html = await HomePageAsync(browser);

        Assert.Contains("Getting started", html);
        Assert.Contains("Mark your home", html);
        Assert.Contains("Add an activity", html);
        Assert.Contains("Nothing planned today.", html);
    }

    [Fact]
    public async Task OnceEverythingIsSetUp_TheGettingStartedStepsAreHidden()
    {
        var (browser, gym) = await SignedInWithHomeAndGymAsync();
        await browser.SubmitFormAsync("/Preferences", "/Preferences", PreferencesForm());
        await browser.AddActivityAsync("Gym", new() { ["LocationId"] = gym.Id.ToString() });

        Assert.DoesNotContain("Getting started", await HomePageAsync(browser));
    }

    // A travel time missing in either direction for a place away from home is still to do (§7.1, P1).
    [Fact]
    public async Task ATravelTimeMissing_IsStillToDo()
    {
        var (browser, _) = await SignedInWithHomeAndGymAsync(withTravelBack: false);

        var html = await HomePageAsync(browser);

        Assert.Contains("Add travel times to your other places", html);
        Assert.Contains("1 of 2 journeys", html);
    }

    // T1 on the home page: gym 18:00-18:50 at Town gym, 25 minutes there, 10 minutes changing, 45 minutes back.
    [Fact]
    public async Task Today_ListsEachActivity_WithWhenToLeaveAndWhenBack()
    {
        var (browser, gym) = await SignedInWithHomeAndGymAsync();
        await browser.AddActivityAsync("Evening gym", new()
        {
            ["LocationId"] = gym.Id.ToString(),
            ["StartDate"] = "2026-10-19",
            ["StartTime"] = "18:00",
            ["DurationHours"] = "0",
            ["DurationMinutes"] = "50",
            ["ChangingMinutes"] = "10",
        });

        var html = await HomePageAsync(browser);

        Assert.Contains("Today, Mon 19 Oct", html);
        Assert.Contains("Evening gym", html);
        Assert.Contains("Leave home 17:35, back 19:45", html);
    }

    // The next 7 days: Tuesday 20 to Monday 26 October. A weekly Monday activity shows today and on the 26th; a one-off
    // on the 27th isn't shown yet.
    [Fact]
    public async Task TheNextSevenDays_AreListedByDay()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        await browser.AddActivityAsync("Team meeting", new() { ["StartDate"] = "2026-10-19", ["Repeats"] = "Weekly", ["RepeatDays[0]"] = "true" });
        await browser.AddActivityAsync("Too far ahead", new() { ["StartDate"] = "2026-10-27" });

        var html = await HomePageAsync(browser);

        Assert.Contains("Mon 26 Oct", html);
        Assert.DoesNotContain("Too far ahead", html);
    }

    // CAL-6: a clash, saved with "Save anyway", is flagged on the activity (in text, not colour alone).
    [Fact]
    public async Task AClash_IsFlagged()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        await browser.AddActivityAsync("Team meeting", new() { ["StartDate"] = "2026-10-19", ["StartTime"] = "15:00" });
        var clashing = TestBrowser.ActivityForm("Call", new() { ["StartDate"] = "2026-10-19", ["StartTime"] = "15:30" });
        var shown = TestBrowser.FormIn(await (await browser.SubmitFormAsync("/Activities/Create", "/Activities/Create", clashing)).Content.ReadAsStringAsync());
        await browser.SubmitAsync(shown, "/Activities/Create", new(clashing) { ["ConfirmClashes"] = "true" });

        var html = await HomePageAsync(browser);

        Assert.Contains("Clashes with Team meeting", html);
        Assert.Contains("Clashes with Call", html);
    }

    // T19: the home page shows only the signed-in user's own activities.
    [Fact]
    public async Task AnotherUsersActivities_AreNotShown()
    {
        var alex = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var alexsTitle = TestBrowser.NewName("Alex gym");
        await alex.AddActivityAsync(alexsTitle, new() { ["StartDate"] = "2026-10-19" });
        var sam = await TestBrowser.OpenSignedInAsync(factory, "sam");

        Assert.DoesNotContain(alexsTitle, await HomePageAsync(sam));
    }

    private async Task<(TestBrowser Browser, Location Gym)> SignedInWithHomeAndGymAsync(bool withTravelBack = true)
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var homeName = TestBrowser.NewName("Home");
        var gymName = TestBrowser.NewName("Town gym");
        await browser.AddLocationAsync(homeName, new() { ["IsHome"] = "true" });
        await browser.AddLocationAsync(gymName);
        var home = (await StoredData.LocationNamedAsync(factory, homeName))!;
        var gym = (await StoredData.LocationNamedAsync(factory, gymName))!;
        await browser.AddTravelLegAsync(home.Id, gym.Id, minutes: 25);
        if (withTravelBack)
        {
            await browser.AddTravelLegAsync(gym.Id, home.Id, minutes: 45);
        }

        return (browser, gym);
    }

    private static Dictionary<string, string> PreferencesForm() => new()
    {
        ["TimeZoneId"] = "Europe/London",
        ["WeekStart"] = "Monday",
        ["Use24HourClock"] = "true",
        ["DistanceUnit"] = "Kilometres",
        ["WeightUnit"] = "Kilograms",
        ["TargetSleepHours"] = "8",
        ["MinimumSleepHours"] = "7",
        ["UsualBedtime"] = "23:00",
        ["UsualWakeTime"] = "07:00",
        ["WindDownMinutes"] = "30",
        ["WakeUpRoutineMinutes"] = "30",
        ["PreferredTimeOfDay"] = "Any",
        ["MinimumHoursBetweenDemandingSessions"] = "24",
        ["MaximumDemandingSessionsPerDay"] = "1",
    };

    private static async Task<string> HomePageAsync(TestBrowser browser)
    {
        var response = await browser.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }
}
