using System.Net;
using FitnessTracker.Web.Data;
using FitnessTracker.Web.Tests.Infrastructure;

namespace FitnessTracker.Web.Tests.Activities;

// SCH-1, first part: saving an activity runs the one check (Domain ActivityCheck). Problems stop the save and show
// next to the field they are about; a clash with another activity (H1) is saved only after "Save anyway" (§7.2, Q2).
// "Today" is Monday 19 October 2026 (FixedClock).
public sealed class ActivityChecksTests(TestAppFactory factory) : IClassFixture<TestAppFactory>
{
    [Fact]
    public async Task Add_ClashingWithAnotherActivity_IsNotSaved_AndSaysWhatItClashesWith()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        await browser.AddActivityAsync("Team meeting", new() { ["Category"] = "Work" });
        var title = TestBrowser.NewName("Gym");

        var response = await browser.AddActivityAsync(title, new() { ["StartTime"] = "07:30" });

        var html = await AssertShowsAnErrorAsync(response);
        Assert.Contains("href=\"#ConfirmClashes\"", html);
        Assert.Contains("On Mon 2 Nov 2026, it overlaps Team meeting (Team meeting is 07:00 to 08:00).", html);
        Assert.Null(await StoredData.ActivityTitledAsync(factory, title));
    }

    // Q2: the user can choose to keep a clash, and it is saved.
    [Fact]
    public async Task Add_ClashingWithAnotherActivity_IsSaved_WithSaveAnyway()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        await browser.AddActivityAsync("Team meeting");
        var title = TestBrowser.NewName("Gym");

        var response = await browser.AddActivityAsync(title, new() { ["StartTime"] = "07:30", ["ConfirmClashes"] = "true" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(await StoredData.ActivityTitledAsync(factory, title));
    }

    // T10: the footprint clashes, not just the activity: a 07:00 gym session with a 45-minute walk home runs into a
    // meeting at home at 08:15.
    [Fact]
    public async Task Add_WhoseTravelRunsIntoAnotherActivity_Clashes()
    {
        var (browser, gym) = await SignedInWithHomeAndGymAsync();
        await browser.AddActivityAsync("Call", new() { ["StartTime"] = "08:15", ["DurationHours"] = "0", ["DurationMinutes"] = "30" });
        var title = TestBrowser.NewName("Gym");

        var response = await browser.AddActivityAsync(title, new() { ["LocationId"] = gym.Id.ToString() });

        var html = await AssertShowsAnErrorAsync(response);
        Assert.Contains("its journey back overlaps Call", html);
    }

    // T11: footprints that only touch don't clash.
    [Fact]
    public async Task Add_BackToBackWithAnotherActivity_IsSaved()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        await browser.AddActivityAsync("Team meeting");
        var title = TestBrowser.NewName("Gym");

        var response = await browser.AddActivityAsync(title, new() { ["StartTime"] = "08:00" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    // An edit is checked against the other activities, not against its own saved version.
    [Fact]
    public async Task Edit_DoesNotClashWithItself_ButDoesWithOthers()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var title = TestBrowser.NewName("Gym");
        await browser.AddActivityAsync(title);
        await browser.AddActivityAsync("Lunch", new() { ["StartTime"] = "12:00" });
        var id = (await StoredData.ActivityTitledAsync(factory, title))!.Id;
        var editUrl = $"/Activities/Edit/{id}";

        var later = await browser.SubmitFormAsync(editUrl, editUrl, TestBrowser.ActivityForm(title, new() { ["StartTime"] = "07:15" }));
        var intoLunch = await browser.SubmitFormAsync(editUrl, editUrl, TestBrowser.ActivityForm(title, new() { ["StartTime"] = "11:30" }));

        Assert.Equal(HttpStatusCode.Redirect, later.StatusCode);
        Assert.Contains("overlaps Lunch", await AssertShowsAnErrorAsync(intoLunch));
    }

    // H4, T17: only the workout has to be inside the opening hours; one starting before the gym opens is refused,
    // next to its start time.
    [Theory]
    [InlineData("06:00", true)]
    [InlineData("05:30", false)]
    public async Task Add_AtAGymWithOpeningHours_MustBeInsideThem(string start, bool saved)
    {
        var (browser, gym) = await SignedInWithHomeAndGymAsync(gymOpens: "06:00", gymCloses: "22:00");
        var title = TestBrowser.NewName("Gym");

        var response = await browser.AddActivityAsync(title, new() { ["LocationId"] = gym.Id.ToString(), ["StartTime"] = start });

        if (saved)
        {
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }
        else
        {
            var html = await AssertShowsAnErrorAsync(response);
            Assert.Contains("href=\"#StartTime\"", html);
            Assert.Contains("is open 06:00 to 22:00 on Mon 2 Nov 2026", html);
            Assert.Null(await StoredData.ActivityTitledAsync(factory, title));
        }
    }

    // §7.1, P1: travel is never guessed as zero. Without a home, or a travel time, the problem says what to add.
    [Fact]
    public async Task Add_AwayFromHome_WithNoHomeSet_IsNotSaved()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var gymName = TestBrowser.NewName("Gym");
        await browser.AddLocationAsync(gymName);
        var gym = (await StoredData.LocationNamedAsync(factory, gymName))!;
        var title = TestBrowser.NewName("Gym");

        var response = await browser.AddActivityAsync(title, new() { ["LocationId"] = gym.Id.ToString() });

        var html = await AssertShowsAnErrorAsync(response);
        Assert.Contains("href=\"#LocationId\"", html);
        Assert.Contains("marked as home", html);
        Assert.Null(await StoredData.ActivityTitledAsync(factory, title));
    }

    [Fact]
    public async Task Add_AwayFromHome_WithATravelTimeMissing_IsNotSaved()
    {
        var (browser, gym) = await SignedInWithHomeAndGymAsync(withTravelBack: false);
        var title = TestBrowser.NewName("Gym");

        var response = await browser.AddActivityAsync(title, new() { ["LocationId"] = gym.Id.ToString() });

        var html = await AssertShowsAnErrorAsync(response);
        Assert.Contains("href=\"#LocationId\"", html);
        Assert.Contains("There&#x27;s no travel time from", html);
        Assert.Null(await StoredData.ActivityTitledAsync(factory, title));
    }

    // H8: with its preparation, an activity of a whole day would take up more than 24 hours.
    [Fact]
    public async Task Add_TakingUpMoreThan24Hours_IsNotSaved()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var title = TestBrowser.NewName("Hike");

        var response = await browser.AddActivityAsync(title, new() { ["DurationHours"] = "24", ["PreparationMinutes"] = "10" });

        var html = await AssertShowsAnErrorAsync(response);
        Assert.Contains("href=\"#DurationHours\"", html);
        Assert.Null(await StoredData.ActivityTitledAsync(factory, title));
    }

    // Home, a gym 25 minutes away (45 minutes back, T1), optionally with opening hours every day.
    private async Task<(TestBrowser Browser, Location Gym)> SignedInWithHomeAndGymAsync(
        string? gymOpens = null, string? gymCloses = null, bool withTravelBack = true)
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var homeName = TestBrowser.NewName("Home");
        var gymName = TestBrowser.NewName("Gym");
        await browser.AddLocationAsync(homeName, new() { ["IsHome"] = "true" });
        await browser.AddLocationAsync(gymName, gymOpens is null ? null : EveryDay(gymOpens, gymCloses!));
        var home = (await StoredData.LocationNamedAsync(factory, homeName))!;
        var gym = (await StoredData.LocationNamedAsync(factory, gymName))!;
        await browser.AddTravelLegAsync(home.Id, gym.Id, minutes: 25);
        if (withTravelBack)
        {
            await browser.AddTravelLegAsync(gym.Id, home.Id, minutes: 45);
        }

        return (browser, gym);
    }

    private static Dictionary<string, string> EveryDay(string opens, string closes)
    {
        var fields = new Dictionary<string, string> { ["HasOpeningHours"] = "true" };
        string[] week = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];
        for (var i = 0; i < week.Length; i++)
        {
            fields[$"OpeningDays[{i}].Day"] = week[i];
            fields[$"OpeningDays[{i}].Closed"] = "false";
            fields[$"OpeningDays[{i}].Opens"] = opens;
            fields[$"OpeningDays[{i}].Closes"] = closes;
        }

        return fields;
    }

    private static async Task<string> AssertShowsAnErrorAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("<title>Error: ", html);
        return html;
    }
}
