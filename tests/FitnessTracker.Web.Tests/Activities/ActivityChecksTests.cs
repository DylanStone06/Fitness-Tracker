using System.Net;
using FitnessTracker.Web.Data;
using FitnessTracker.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FitnessTracker.Web.Tests.Activities;

// SCH-1, first part: saving an activity runs the one check (Domain ActivityCheck). Problems stop the save and show
// next to the field they are about; a clash with another activity (H1) is saved only after "Save anyway" (Â§7.2, Q2).
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

    // Q2: the user can choose to keep a clash once it has been shown, and it is saved.
    [Fact]
    public async Task Add_ClashingWithAnotherActivity_IsSaved_WithSaveAnyway()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        await browser.AddActivityAsync("Team meeting");
        var title = TestBrowser.NewName("Gym");
        var clashing = new Dictionary<string, string> { ["StartTime"] = "07:30" };
        var shown = TestBrowser.FormIn(await (await browser.AddActivityAsync(title, clashing)).Content.ReadAsStringAsync());

        var response = await browser.SubmitAsync(shown, "/Activities/Create", TestBrowser.ActivityForm(title, new(clashing) { ["ConfirmClashes"] = "true" }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(await StoredData.ActivityTitledAsync(factory, title));
    }

    // Â§7.2, Q2: "Save anyway" accepts the clashes that were shown, not whatever the posted form clashes with. Ticking
    // it while moving the activity into a different clash (Lunch, never shown) shows that clash and asks again.
    [Fact]
    public async Task Add_WithSaveAnyway_ButADifferentClash_IsNotSaved_AndShowsTheNewClash()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        await browser.AddActivityAsync("Team meeting");
        await browser.AddActivityAsync("Lunch", new() { ["StartTime"] = "12:00" });
        var title = TestBrowser.NewName("Gym");
        var shown = TestBrowser.FormIn(await (await browser.AddActivityAsync(title, new() { ["StartTime"] = "07:30" })).Content.ReadAsStringAsync());

        var response = await browser.SubmitAsync(shown, "/Activities/Create",
            TestBrowser.ActivityForm(title, new() { ["StartTime"] = "11:30", ["ConfirmClashes"] = "true" }));

        var html = await AssertShowsAnErrorAsync(response);
        Assert.Contains("overlaps Lunch", html);
        Assert.Null(await StoredData.ActivityTitledAsync(factory, title));
    }

    // A "Save anyway" posted without any clashes having been shown accepts nothing.
    [Fact]
    public async Task Add_WithSaveAnywayTickedBeforeAnyClashWasShown_IsNotSaved()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        await browser.AddActivityAsync("Team meeting");
        var title = TestBrowser.NewName("Gym");

        var response = await browser.AddActivityAsync(title, new() { ["StartTime"] = "07:30", ["ConfirmClashes"] = "true" });

        Assert.Contains("overlaps Team meeting", await AssertShowsAnErrorAsync(response));
        Assert.Null(await StoredData.ActivityTitledAsync(factory, title));
    }

    // T10: the footprint clashes, not just the activity: a 07:00 gym session with a 45-minute walk home runs into a
    // meeting at home at 08:15.
    [Fact]
    public async Task Add_WhoseTravelRunsIntoAnotherActivity_Clashes()
    {
        var (browser, gym, _) = await SignedInWithHomeAndGymAsync();
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
        var (browser, gym, _) = await SignedInWithHomeAndGymAsync(gymOpens: "06:00", gymCloses: "22:00");
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

    // Â§7.1, P1: travel is never guessed as zero. Without a home, or a travel time, the problem says what to add.
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
        var (browser, gym, _) = await SignedInWithHomeAndGymAsync(withTravelBack: false);
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

    // P1: another activity that can only be checked in part doesn't stop the save, but the user is told. Here the
    // gym session's travel time back has been deleted, so its walk home can't be counted against dinner.
    [Fact]
    public async Task Add_WhenAnotherActivitysTravelIsUnknown_SavesAndSaysSo()
    {
        var (browser, gym, home) = await SignedInWithHomeAndGymAsync();
        await browser.AddActivityAsync("Gym", new() { ["LocationId"] = gym.Id.ToString(), ["StartTime"] = "18:00" });
        var back = (await StoredData.TravelLegAsync(factory, gym.Id, home.Id))!.Id;
        await browser.SubmitFormAsync($"/TravelLegs/Delete/{back}", $"/TravelLegs/Delete/{back}", []);

        var response = await browser.AddActivityAsync(TestBrowser.NewName("Dinner"), new() { ["StartTime"] = "19:30" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var list = await (await browser.GetAsync("/Activities")).Content.ReadAsStringAsync();
        Assert.Contains("Gym was checked without its travel", list);
    }

    // One stored activity the app can't read (edited by hand, say) mustn't stop every other save, or the list.
    [Fact]
    public async Task Add_WhenAnotherStoredActivityCantBeRead_IsStillChecked_AndSaysSo()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var unreadable = TestBrowser.NewName("Old");
        await browser.AddActivityAsync(unreadable, new() { ["Repeats"] = "Daily" });
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Activities.IgnoreQueryFilters()
                .Where(activity => activity.Title == unreadable)
                .ExecuteUpdateAsync(set => set.SetProperty(activity => activity.RepeatRule, "FREQ=MONTHLY"));
        }

        var response = await browser.AddActivityAsync(TestBrowser.NewName("Gym"), new() { ["StartTime"] = "12:00" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var list = await browser.GetAsync("/Activities");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Contains($"{unreadable} couldn", await list.Content.ReadAsStringAsync());
    }

    // Only activities near the dates checked are read, but a repeating one that started long before still counts: a
    // weekly Monday meeting from 5 October clashes with a gym session on Monday 2 November.
    [Fact]
    public async Task Add_ClashingWithARepeatingActivityThatStartedEarlier_Clashes()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        await browser.AddActivityAsync("Team meeting", new() { ["StartDate"] = "2026-10-05", ["Repeats"] = "Weekly", ["RepeatDays[0]"] = "true" });

        var response = await browser.AddActivityAsync(TestBrowser.NewName("Gym"), new() { ["StartTime"] = "07:30" });

        Assert.Contains("overlaps Team meeting", await AssertShowsAnErrorAsync(response));
    }

    // Today is 19 October 2026: a gym session added for 12 October still needs "Save anyway" to clash with that
    // day's meeting.
    [Fact]
    public async Task Add_InThePast_ClashingWithAnotherActivity_NeedsSaveAnyway()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        await browser.AddActivityAsync("Team meeting", new() { ["StartDate"] = "2026-10-12" });
        var title = TestBrowser.NewName("Gym");

        var response = await browser.AddActivityAsync(title, new() { ["StartDate"] = "2026-10-12", ["StartTime"] = "07:30" });

        Assert.Contains("overlaps Team meeting", await AssertShowsAnErrorAsync(response));
        Assert.Null(await StoredData.ActivityTitledAsync(factory, title));
    }

    // T19: the check only ever looks at the signed-in user's own activities: Sam's meeting at the same time doesn't
    // clash with Alex's gym session.
    [Fact]
    public async Task Add_IsNotCheckedAgainstAnotherUsersActivities()
    {
        var sam = await TestBrowser.OpenSignedInAsync(factory, "sam");
        await sam.AddActivityAsync(TestBrowser.NewName("Sam meeting"));
        var alex = await TestBrowser.OpenSignedInAsync(factory, "alex");

        var response = await alex.AddActivityAsync(TestBrowser.NewName("Gym"), new() { ["StartTime"] = "07:30" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    // Q7: a whole day from midnight is 25 hours on Sunday 25 October 2026, when the clocks go back. It can be entered,
    // and is saved for that day; the same 25 hours on the 26th is refused next to its length.
    [Theory]
    [InlineData("2026-10-25", true)]
    [InlineData("2026-10-26", false)]
    public async Task Add_TheWhole25HourDay_IsSaved_OnlyOnTheDayTheClocksGoBack(string date, bool saved)
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var title = TestBrowser.NewName("Festival");

        var response = await browser.AddActivityAsync(title, new() { ["StartDate"] = date, ["StartTime"] = "00:00", ["DurationHours"] = "25" });

        if (saved)
        {
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal(25 * 60, (await StoredData.ActivityTitledAsync(factory, title))?.LengthMinutes);
        }
        else
        {
            Assert.Contains("href=\"#DurationHours\"", await AssertShowsAnErrorAsync(response));
            Assert.Null(await StoredData.ActivityTitledAsync(factory, title));
        }
    }

    // Home, a gym 25 minutes away (45 minutes back, T1), optionally with opening hours every day.
    private async Task<(TestBrowser Browser, Location Gym, Location Home)> SignedInWithHomeAndGymAsync(
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

        return (browser, gym, home);
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
