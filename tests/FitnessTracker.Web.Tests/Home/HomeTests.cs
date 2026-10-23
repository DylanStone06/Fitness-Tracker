using System.Net;
using FitnessTracker.Web.Data;
using FitnessTracker.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
        var (browser, gym, _) = await SignedInWithHomeAndGymAsync();
        await browser.SubmitFormAsync("/Preferences", "/Preferences", PreferencesForm());
        await browser.AddActivityAsync("Gym", new() { ["LocationId"] = gym.Id.ToString() });

        Assert.DoesNotContain("Getting started", await HomePageAsync(browser));
    }

    // A travel time missing in either direction for a place away from home is still to do (§7.1, P1).
    [Fact]
    public async Task ATravelTimeMissing_IsStillToDo()
    {
        var (browser, _, _) = await SignedInWithHomeAndGymAsync(withTravelBack: false);

        var html = await HomePageAsync(browser);

        Assert.Contains("Add travel times to your other places", html);
        Assert.Contains("1 of 2 journeys", html);
    }

    // T1 on the home page: gym 18:00-18:50 at Town gym, 25 minutes there, 10 minutes changing, 45 minutes back.
    [Fact]
    public async Task Today_ListsEachActivity_WithWhenToLeaveAndWhenBack()
    {
        var (browser, gym, _) = await SignedInWithHomeAndGymAsync();
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

    // Getting ready happens at home before leaving (§7.1), and a late session gets back the next day (§8): leave at
    // 22:05, back 22:30 + 50 + 10 + 45 minutes = 00:15 the next day (BST).
    [Theory]
    [InlineData("18:00", "15", "Leave home 17:35, back 19:45")]
    [InlineData("22:30", "0", "Leave home 22:05, back 00:15 the next day")]
    public async Task Today_SaysWhenToLeave_AfterGettingReady_AndWhichDayYouAreBack(string start, string preparation, string expected)
    {
        var (browser, gym, _) = await SignedInWithHomeAndGymAsync();
        await browser.AddActivityAsync("Gym", new()
        {
            ["LocationId"] = gym.Id.ToString(),
            ["StartDate"] = "2026-10-19",
            ["StartTime"] = start,
            ["DurationHours"] = "0",
            ["DurationMinutes"] = "50",
            ["PreparationMinutes"] = preparation,
            ["ChangingMinutes"] = "10",
        });

        Assert.Contains(expected, await HomePageAsync(browser));
    }

    // P1: travel that can't be worked out is explained. Here home has been un-marked since the activity was saved.
    [Fact]
    public async Task AnActivityAwayFromHome_WithNoHomeMarked_SaysWhy()
    {
        var (browser, gym, home) = await SignedInWithHomeAndGymAsync();
        await browser.AddActivityAsync("Gym", new() { ["LocationId"] = gym.Id.ToString(), ["StartDate"] = "2026-10-19", ["StartTime"] = "18:00" });
        await browser.SubmitFormAsync($"/Locations/Edit/{home.Id}", $"/Locations/Edit/{home.Id}", new()
        {
            ["Name"] = home.Name,
            ["IsHome"] = "false",
            ["HasOpeningHours"] = "false",
        });

        Assert.Contains("none of your locations is marked as home", await HomePageAsync(browser));
    }

    // H8 after the fact: a 16-hour day at the gym was fine with a 45-minute walk home, but the travel time back has
    // since been changed to 600 minutes. The home page says it can't be checked rather than leaving it out silently.
    [Fact]
    public async Task AnActivityNowOver24Hours_SaysSo()
    {
        var (browser, gym, home) = await SignedInWithHomeAndGymAsync();
        await browser.AddActivityAsync("Festival", new()
        {
            ["LocationId"] = gym.Id.ToString(),
            ["StartDate"] = "2026-10-20",
            ["StartTime"] = "06:00",
            ["DurationHours"] = "16",
        });
        var back = (await StoredData.TravelLegAsync(factory, gym.Id, home.Id))!.Id;
        await browser.SubmitFormAsync($"/TravelLegs/Edit/{back}", $"/TravelLegs/Edit/{back}", new()
        {
            ["FromLocationId"] = gym.Id.ToString(),
            ["ToLocationId"] = home.Id.ToString(),
            ["Minutes"] = "600",
            ["Mode"] = "Walk",
        });

        Assert.Contains("more than a day allows", await HomePageAsync(browser));
    }

    // A stored activity the app can't use (its length edited by hand to less than zero) is left out with a note,
    // rather than stopping the page.
    [Fact]
    public async Task AnActivityThatCantBeRead_IsLeftOut_AndTheHomePageStillOpens()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var title = TestBrowser.NewName("Broken");
        await browser.AddActivityAsync(title, new() { ["StartDate"] = "2026-10-19" });
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Activities.IgnoreQueryFilters()
                .Where(activity => activity.Title == title)
                .ExecuteUpdateAsync(set => set.SetProperty(activity => activity.LengthMinutes, -30));
        }

        Assert.Contains($"{title} couldn&#x27;t be read", await HomePageAsync(browser));
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

    // The greeting follows the time where the user is: 12:00 UTC is 13:00 in London, 08:00 in New York, 21:00 in Tokyo.
    [Theory]
    [InlineData("Europe/London", "Good afternoon")]
    [InlineData("America/New_York", "Good morning")]
    [InlineData("Asia/Tokyo", "Good evening")]
    public async Task TheGreeting_FollowsTheTimeWhereTheUserIs(string zone, string expected)
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        await browser.SubmitFormAsync("/Preferences", "/Preferences", new(PreferencesForm()) { ["TimeZoneId"] = zone });

        Assert.Contains(expected, await HomePageAsync(browser));
    }

    // GOL-2, "the next activity": at 13:00 the 09:00 meeting is over, so the evening gym session is next, with when to
    // leave (T1).
    [Fact]
    public async Task NextUp_IsTheNextActivity_WithWhenToLeave()
    {
        var (browser, gym, _) = await SignedInWithHomeAndGymAsync();
        await browser.AddActivityAsync("Morning meeting", new() { ["StartDate"] = "2026-10-19", ["StartTime"] = "09:00" });
        await browser.AddActivityAsync("Evening gym", new()
        {
            ["LocationId"] = gym.Id.ToString(),
            ["StartDate"] = "2026-10-19",
            ["StartTime"] = "18:00",
            ["DurationHours"] = "0",
            ["DurationMinutes"] = "50",
            ["ChangingMinutes"] = "10",
        });

        var nextUp = Section(await HomePageAsync(browser), "next-up-heading");

        Assert.Contains("Evening gym", nextUp);
        Assert.Contains("Leave home 17:35, back 19:45", nextUp);
        Assert.DoesNotContain("Morning meeting", nextUp);
    }

    // The Next up card says when: an hour-long swim from 12:30 BST (11:30 UTC) is under way at 13:00 BST; later ones
    // say today, tomorrow, or their date.
    [Theory]
    [InlineData("2026-10-19", "12:30", "Under way")]
    [InlineData("2026-10-19", "18:00", "Today")]
    [InlineData("2026-10-20", "09:00", "Tomorrow")]
    [InlineData("2026-10-22", "09:00", "Thu 22 Oct")]
    public async Task NextUp_SaysWhen(string date, string time, string expected)
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        await browser.AddActivityAsync("Swim", new() { ["StartDate"] = date, ["StartTime"] = time });

        Assert.Contains($"· {expected}</span>", Section(await HomePageAsync(browser), "next-up-heading"));
    }

    [Fact]
    public async Task NextUp_IsLeftOut_WhenNothingIsLeft()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        await browser.AddActivityAsync("Morning meeting", new() { ["StartDate"] = "2026-10-19", ["StartTime"] = "09:00" });

        Assert.DoesNotContain("next-up-heading", await HomePageAsync(browser));
    }

    // CAL-2: each activity shows its category by name (its colour is never the only signal), and opens its edit page.
    [Fact]
    public async Task EachActivity_ShowsItsCategory_AndOpensItsEditPage()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var title = TestBrowser.NewName("Catch-up");
        await browser.AddActivityAsync(title, new() { ["StartDate"] = "2026-10-19", ["StartTime"] = "19:00", ["Category"] = "Social" });
        var id = (await StoredData.ActivityTitledAsync(factory, title))!.Id;

        var html = await HomePageAsync(browser);

        Assert.Contains($"href=\"/Activities/Edit/{id}\"", html);
        Assert.Contains("class=\"ft-chip\">Social</span>", html);
    }

    // CAL-4: an occurrence of a repeating activity opens its own page, to change just that one.
    [Fact]
    public async Task ARepeatingActivity_OpensThatOccurrence()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var title = TestBrowser.NewName("Daily walk");
        await browser.AddActivityAsync(title, new() { ["StartDate"] = "2026-10-19", ["StartTime"] = "19:00", ["Repeats"] = "Daily" });
        var id = (await StoredData.ActivityTitledAsync(factory, title))!.Id;

        var html = await HomePageAsync(browser);

        Assert.Contains($"href=\"/Activities/Occurrence/{id}?date=2026-10-20\"", html);
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

    private async Task<(TestBrowser Browser, Location Gym, Location Home)> SignedInWithHomeAndGymAsync(bool withTravelBack = true)
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

        return (browser, gym, home);
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

    /// <summary>One section of the page, from its heading (found by id) to the section's end.</summary>
    private static string Section(string html, string headingId)
    {
        var start = html.IndexOf($"id=\"{headingId}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"No heading with the id {headingId}.");
        return html[start..html.IndexOf("</section>", start, StringComparison.Ordinal)];
    }

    private static async Task<string> HomePageAsync(TestBrowser browser)
    {
        var response = await browser.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }
}
