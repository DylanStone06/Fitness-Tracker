using System.Net;
using FitnessTracker.Web.Data;
using FitnessTracker.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FitnessTracker.Web.Tests.Goals;

// FIT-5, Q24, Q37: each weekly goal's week, from the sessions of the activities linked to it and their marks. The tests'
// "today" is Monday 19 October 2026 at 13:00 in London, so this week is 19-25 October.
public sealed class GoalProgressPageTests(TestAppFactory factory) : IClassFixture<TestAppFactory>
{
    [Fact]
    public async Task TheGoalsPage_ShowsEachGoalsWeek()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var title = TestBrowser.NewName("Gym three times");
        var goal = await AddGoalAsync(browser, title);
        var done = await AddSessionAsync(browser, goal, "2026-10-19", "07:00");
        await AddSessionAsync(browser, goal, "2026-10-19", "09:00");
        var skipped = await AddSessionAsync(browser, goal, "2026-10-20", "07:00");
        await AddSessionAsync(browser, goal, "2026-10-21", "07:00");
        await AddSessionAsync(browser, goal: null, "2026-10-22", "07:00");
        await browser.MarkSessionAsync(done, "2026-10-19", "Done");
        await browser.MarkSessionAsync(skipped, "2026-10-20", "Skipped");

        var html = await PageAsync(browser, "/Goals");

        Assert.Contains("This week, 19 – 25 Oct 2026", html);
        Assert.Contains("1 of 3 done", html);
        Assert.Contains("1 skipped · 1 not logged · 1 to come", html);
        Assert.Matches($"""<progress[^>]*max="3"[^>]*value="1"[^>]*aria-label="{title}: 1 of 3 done"[^>]*>""", html);
    }

    // Q20: meal prep has no statuses, so its sessions count as planned.
    [Fact]
    public async Task AGoalWithoutStatuses_CountsItsPlannedSessions()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var goal = await AddGoalAsync(browser, TestBrowser.NewName("Batch cooking"), new() { ["Category"] = "MealPrep", ["SessionsPerWeek"] = "2" });
        await AddSessionAsync(browser, goal, "2026-10-24", "10:00", category: "MealPrep");

        var html = await PageAsync(browser, "/Goals");

        Assert.Contains("1 of 2 planned", html);
        Assert.Contains("1 not planned yet", html);
    }

    // T19: only the user's own goals and sessions: another user's goal, with a session done, isn't shown or counted.
    [Fact]
    public async Task AnotherUsersGoalsAndSessions_ArentShown()
    {
        var sam = await TestBrowser.OpenSignedInAsync(factory, "sam");
        var samsGoal = await AddGoalAsync(sam, TestBrowser.NewName("Sam's gym"));
        var samsSession = await AddSessionAsync(sam, samsGoal, "2026-10-19", "07:00");
        await sam.MarkSessionAsync(samsSession, "2026-10-19", "Done");
        var alex = await TestBrowser.OpenSignedInAsync(factory, "alex");
        await AddGoalAsync(alex, TestBrowser.NewName("Alex's gym"));

        var html = await PageAsync(alex, "/Goals");

        Assert.DoesNotContain("Sam's gym", html);
        Assert.Contains("0 of 3 done", html);
        Assert.Contains("3 not planned yet", html);
    }

    // P1: an activity for a goal that can't be read (edited by hand) is left out of its week, and said.
    [Fact]
    public async Task AnActivityThatCantBeRead_IsSaid()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var goal = await AddGoalAsync(browser, TestBrowser.NewName("Gym three times"));
        var title = TestBrowser.NewName("Gym");
        await browser.AddActivityAsync(title, new() { ["StartDate"] = "2026-10-20", ["GoalId"] = goal.ToString() });
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Activities.IgnoreQueryFilters()
                .Where(activity => activity.Title == title)
                .ExecuteUpdateAsync(set => set.SetProperty(activity => activity.OccurrenceChanges, "not readable"));
        }

        var html = await PageAsync(browser, "/Goals");

        Assert.Contains($"{title} couldn't be read, so it's left out.", html);
        Assert.Contains("0 of 3 done", html);
    }

    private async Task<Guid> AddGoalAsync(TestBrowser browser, string title, Dictionary<string, string>? changes = null)
    {
        Assert.Equal(HttpStatusCode.Redirect, (await browser.AddGoalAsync(title, changes)).StatusCode);
        return (await StoredData.GoalTitledAsync(factory, title))!.Id;
    }

    // A one-hour one-off session on that date and time, for the goal (or none).
    private async Task<Guid> AddSessionAsync(TestBrowser browser, Guid? goal, string date, string time, string category = "Gym")
    {
        var title = TestBrowser.NewName(category);
        var response = await browser.AddActivityAsync(title, new()
        {
            ["Category"] = category,
            ["StartDate"] = date,
            ["StartTime"] = time,
            ["GoalId"] = goal?.ToString() ?? "",
        });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return (await StoredData.ActivityTitledAsync(factory, title))!.Id;
    }

    // The page's text with its characters decoded ("·" rather than "&#xB7;").
    private static async Task<string> PageAsync(TestBrowser browser, string url)
    {
        var response = await browser.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }
}
