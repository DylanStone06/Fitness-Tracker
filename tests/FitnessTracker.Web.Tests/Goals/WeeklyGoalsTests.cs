using System.Net;
using FitnessTracker.Web.Data;
using FitnessTracker.Web.Tests.Infrastructure;

namespace FitnessTracker.Web.Tests.Goals;

// GOL-1: what the user wants to do each week, with no time of its own: "Gym, 3 times a week, an hour each". The
// planner (SCH-3) finds the times; accepting one adds it every week or for that week only (Q12). Another user's goals
// are covered by T19 in Security/AccessControlTests.
public sealed class WeeklyGoalsTests(TestAppFactory factory) : IClassFixture<TestAppFactory>
{
    [Fact]
    public async Task Add_SavesEveryField()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var gym = await AddLocationAsync(browser, "Town gym");
        var title = TestBrowser.NewName("Strength");

        var response = await browser.AddGoalAsync(title, new()
        {
            ["Category"] = "Gym",
            ["LocationId"] = gym.ToString(),
            ["SessionsPerWeek"] = "3",
            ["DurationHours"] = "0",
            ["DurationMinutes"] = "50",
            ["PreparationMinutes"] = "10",
            ["ChangingMinutes"] = "15",
            ["Priority"] = "High",
            ["IsDemanding"] = "true",
            ["PreferredDays[0]"] = "true",
            ["PreferredDays[2]"] = "true",
            ["PreferredDays[4]"] = "true",
            ["PreferredTimeOfDay"] = "Evening",
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var saved = await StoredData.GoalTitledAsync(factory, title);
        Assert.NotNull(saved);
        Assert.Equal(ActivityCategory.Gym, saved.Category);
        Assert.Equal(gym, saved.LocationId);
        Assert.Equal(3, saved.SessionsPerWeek);
        Assert.Equal(50, saved.SessionMinutes);
        Assert.Equal((10, 15), (saved.PreparationMinutes, saved.ChangingMinutes));
        Assert.Equal(ActivityPriority.High, saved.Priority);
        Assert.True(saved.IsDemanding);
        Assert.Equal(DaysOfWeek.Monday | DaysOfWeek.Wednesday | DaysOfWeek.Friday, saved.PreferredDays);
        Assert.Equal(PreferredTimeOfDay.Evening, saved.PreferredTimeOfDay);
    }

    // ACC-6: getting ready and changing left blank take the category's default, as for an activity (Run: 5 and 10).
    [Fact]
    public async Task Add_WithBlankBuffers_UsesTheCategorysDefault()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var title = TestBrowser.NewName("Runs");

        await browser.AddGoalAsync(title, new() { ["Category"] = "Run" });

        var saved = await StoredData.GoalTitledAsync(factory, title);
        Assert.Equal(CategoryBuffers.BuiltIn(ActivityCategory.Run), (saved!.PreparationMinutes, saved.ChangingMinutes));
    }

    [Theory]
    [InlineData("Title", "", "Enter a name for the goal.")]
    [InlineData("SessionsPerWeek", "0", "Enter how many times a week, from 1 to 14.")]
    [InlineData("SessionsPerWeek", "15", "Enter how many times a week, from 1 to 14.")]
    [InlineData("DurationHours", "0", "Enter how long each time takes: at least 1 minute and at most 24 hours.")]
    public async Task Add_RefusesWhatCantBePlanned(string field, string value, string message)
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var title = TestBrowser.NewName("Goal");

        var response = await browser.AddGoalAsync(title, new() { [field] = value, ["DurationMinutes"] = "0" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(System.Net.WebUtility.HtmlEncode(message), await response.Content.ReadAsStringAsync());
        Assert.Null(await StoredData.GoalTitledAsync(factory, title));
    }

    // A form refers to the user's own locations only (architecture rule 5): another user's is "choose from the list".
    [Fact]
    public async Task Add_AtAnotherUsersLocation_IsRefused()
    {
        var sam = await TestBrowser.OpenSignedInAsync(factory, "sam");
        var samsGym = await AddLocationAsync(sam, "Sam's gym");
        var alex = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var title = TestBrowser.NewName("Gym");

        var response = await alex.AddGoalAsync(title, new() { ["LocationId"] = samsGym.ToString() });

        Assert.Contains("Choose a location from the list.", await response.Content.ReadAsStringAsync());
        Assert.Null(await StoredData.GoalTitledAsync(factory, title));
    }

    // The list says each goal in a sentence: how often, how long, where, and when it's preferred.
    [Fact]
    public async Task TheList_DescribesEachGoal()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var gym = await AddLocationAsync(browser, "Town gym");
        var title = TestBrowser.NewName("Strength");
        await browser.AddGoalAsync(title, new()
        {
            ["LocationId"] = gym.ToString(),
            ["DurationHours"] = "1",
            ["DurationMinutes"] = "30",
            ["PreferredDays[0]"] = "true",
            ["PreferredDays[3]"] = "true",
            ["PreferredTimeOfDay"] = "Evening",
        });

        var html = await (await browser.GetAsync("/Goals")).Content.ReadAsStringAsync();

        Assert.Contains(title, html);
        Assert.Contains("3 times a week, 1 h 30 min each", html);
        Assert.Contains("Town gym", html);
        Assert.Contains("Preferably Mon, Thu, in the evening", html);
    }

    [Fact]
    public async Task TheList_WithNoGoals_SaysWhatTheyAreFor()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "nobody");

        var html = await (await browser.GetAsync("/Goals")).Content.ReadAsStringAsync();

        Assert.Contains("You haven't added any weekly goals yet.", html);
    }

    [Fact]
    public async Task Edit_SavesTheChange()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var title = TestBrowser.NewName("Runs");
        await browser.AddGoalAsync(title);
        var id = (await StoredData.GoalTitledAsync(factory, title))!.Id;

        var response = await browser.SubmitFormAsync($"/Goals/Edit/{id}", $"/Goals/Edit/{id}", TestBrowser.GoalForm(title, new() { ["SessionsPerWeek"] = "2" }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(2, (await StoredData.GoalTitledAsync(factory, title))!.SessionsPerWeek);
    }

    // Requirements §9: a goal changed in another tab since the form was opened isn't overwritten unseen.
    [Fact]
    public async Task Edit_FromAnOutOfDateTab_IsNotSaved()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var title = TestBrowser.NewName("Runs");
        await browser.AddGoalAsync(title);
        var id = (await StoredData.GoalTitledAsync(factory, title))!.Id;
        var oldTab = await browser.LoadFormAsync($"/Goals/Edit/{id}");
        await browser.SubmitFormAsync($"/Goals/Edit/{id}", $"/Goals/Edit/{id}", TestBrowser.GoalForm(title, new() { ["SessionsPerWeek"] = "2" }));

        var response = await browser.SubmitAsync(oldTab, $"/Goals/Edit/{id}", TestBrowser.GoalForm(title, new() { ["SessionsPerWeek"] = "5" }));

        Assert.Contains("changed in another tab", await response.Content.ReadAsStringAsync());
        Assert.Equal(2, (await StoredData.GoalTitledAsync(factory, title))!.SessionsPerWeek);
    }

    [Fact]
    public async Task Delete_RemovesIt()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var title = TestBrowser.NewName("Runs");
        await browser.AddGoalAsync(title);
        var id = (await StoredData.GoalTitledAsync(factory, title))!.Id;

        var response = await browser.SubmitFormAsync($"/Goals/Delete/{id}", $"/Goals/Delete/{id}", []);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Null(await StoredData.GoalTitledAsync(factory, title));
    }

    private async Task<Guid> AddLocationAsync(TestBrowser browser, string name)
    {
        var unique = TestBrowser.NewName(name);
        Assert.Equal(HttpStatusCode.Redirect, (await browser.AddLocationAsync(unique)).StatusCode);
        return (await StoredData.LocationNamedAsync(factory, unique))!.Id;
    }
}
