using System.Net;
using System.Text.RegularExpressions;
using FitnessTracker.Web.Tests.Infrastructure;

namespace FitnessTracker.Web.Tests.Goals;

// GOL-1, SCH-4: an activity added for a weekly goal remembers it, so it counts towards the goal when the week is
// planned. A suggestion is saved through the ordinary activity form (SCH-1: the one check on save), filled in from the
// goal; the form can also be opened filled in to change it first ("Change"), and goes back to the plan once saved.
public sealed class ActivitiesForAGoalTests(TestAppFactory factory) : IClassFixture<TestAppFactory>
{
    [Fact]
    public async Task AnActivityAddedForAGoal_RemembersIt()
    {
        var (browser, goal) = await WithAGoalAsync();
        var title = TestBrowser.NewName("Gym");

        await browser.AddActivityAsync(title, new() { ["GoalId"] = goal.ToString() });

        Assert.Equal(goal, (await StoredData.ActivityTitledAsync(factory, title))!.GoalId);
    }

    // Architecture rule 5: a form refers to the user's own records only; another user's goal is "not found", so the
    // activity is saved without it (the goal is a hidden link the user can't choose, so there's nothing to correct).
    [Fact]
    public async Task AnotherUsersGoal_IsNotLinked()
    {
        var (_, samsGoal) = await WithAGoalAsync("sam");
        var alex = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var title = TestBrowser.NewName("Gym");

        var response = await alex.AddActivityAsync(title, new() { ["GoalId"] = samsGoal.ToString() });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Null((await StoredData.ActivityTitledAsync(factory, title))!.GoalId);
    }

    // A goal deleted in another tab while an activity's form is open doesn't stop the save: the link is dropped, as the
    // database dropped it (code review, Phase 6).
    [Fact]
    public async Task AGoalDeletedMeanwhile_DoesntStopTheSave()
    {
        var (browser, goal) = await WithAGoalAsync();
        var title = TestBrowser.NewName("Gym");
        await browser.AddActivityAsync(title, new() { ["GoalId"] = goal.ToString() });
        var id = (await StoredData.ActivityTitledAsync(factory, title))!.Id;
        var openForm = await browser.LoadFormAsync($"/Activities/Edit/{id}");
        await browser.SubmitFormAsync($"/Goals/Delete/{goal}", $"/Goals/Delete/{goal}", []);

        var response = await browser.SubmitAsync(openForm, $"/Activities/Edit/{id}", TestBrowser.ActivityForm(title, new() { ["StartTime"] = "08:00", ["GoalId"] = goal.ToString() }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Null((await StoredData.ActivityTitledAsync(factory, title))!.GoalId);
    }

    [Fact]
    public async Task EditingIt_KeepsTheGoal()
    {
        var (browser, goal) = await WithAGoalAsync();
        var title = TestBrowser.NewName("Gym");
        await browser.AddActivityAsync(title, new() { ["GoalId"] = goal.ToString() });
        var id = (await StoredData.ActivityTitledAsync(factory, title))!.Id;

        var form = await browser.GetAsync($"/Activities/Edit/{id}");
        await browser.SubmitFormAsync($"/Activities/Edit/{id}", $"/Activities/Edit/{id}",
            TestBrowser.ActivityForm(title, new() { ["StartTime"] = "08:00", ["GoalId"] = HiddenValue(await form.Content.ReadAsStringAsync(), "GoalId") ?? "" }));

        Assert.Equal(goal, (await StoredData.ActivityTitledAsync(factory, title))!.GoalId);
    }

    // "Change" on a suggestion: the form, filled in from the goal at the suggested time, every week on that day.
    [Fact]
    public async Task TheFormForAGoal_IsFilledInFromIt()
    {
        var (browser, goal) = await WithAGoalAsync(changes: new()
        {
            ["Category"] = "Run",
            ["DurationHours"] = "0",
            ["DurationMinutes"] = "40",
            ["PreparationMinutes"] = "5",
            ["ChangingMinutes"] = "15",
            ["IsDemanding"] = "true",
            ["Priority"] = "High",
        });

        var html = await (await browser.GetAsync($"/Activities/Create?goal={goal}&date=2026-11-04&time=18:30&everyWeek=true&returnToPlan=true")).Content.ReadAsStringAsync();

        Assert.Equal("Run", SelectedValue(html, "Category"));
        Assert.Equal("2026-11-04", InputValue(html, "StartDate"));
        Assert.Equal("18:30", InputValue(html, "StartTime"));
        Assert.Equal(("0", "40"), (InputValue(html, "DurationHours"), InputValue(html, "DurationMinutes")));
        Assert.Equal(("5", "15"), (InputValue(html, "PreparationMinutes"), InputValue(html, "ChangingMinutes")));
        Assert.True(IsChecked(html, """id="Repeats-weekly" """.TrimEnd()), "Weekly isn't chosen");
        Assert.True(IsChecked(html, """name="RepeatDays[2]" type="checkbox" """.TrimEnd()) || IsChecked(html, "id=\"RepeatDays_2_\""), "Wednesday isn't ticked");
        Assert.False(IsChecked(html, "id=\"RepeatDays_0_\""), "Monday is ticked");
        Assert.True(IsChecked(html, "id=\"IsFixed-movable\""), "Movable isn't chosen");
        Assert.Equal(goal.ToString(), HiddenValue(html, "GoalId"));
        Assert.Equal("true", HiddenValue(html, "ReturnToPlan")?.ToLowerInvariant());
    }

    // Saved from the plan, it goes back to the plan, on the week it's in.
    [Fact]
    public async Task SavedFromThePlan_ItGoesBackToThePlan()
    {
        var (browser, goal) = await WithAGoalAsync();

        var response = await browser.AddActivityAsync(TestBrowser.NewName("Gym"), new()
        {
            ["GoalId"] = goal.ToString(),
            ["StartDate"] = "2026-11-04",
            ["ReturnToPlan"] = "true",
        });

        Assert.Equal("/Plan?date=2026-11-04", response.Headers.Location?.OriginalString);
    }

    private async Task<(TestBrowser Browser, Guid Goal)> WithAGoalAsync(string user = "alex", Dictionary<string, string>? changes = null)
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, user);
        var title = TestBrowser.NewName("Gym goal");
        Assert.Equal(HttpStatusCode.Redirect, (await browser.AddGoalAsync(title, changes)).StatusCode);
        return (browser, (await StoredData.GoalTitledAsync(factory, title))!.Id);
    }

    private static string? InputValue(string html, string name)
    {
        var input = Regex.Match(html, $"""<input[^>]*\sname="{Regex.Escape(name)}"[^>]*>""");
        var value = Regex.Match(input.Value, """\svalue="([^"]*)" """.TrimEnd());
        return value.Success ? WebUtility.HtmlDecode(value.Groups[1].Value) : null;
    }

    private static string? HiddenValue(string html, string name)
    {
        var input = Regex.Match(html, $"""<input[^>]*type="hidden"[^>]*\sname="{Regex.Escape(name)}"[^>]*>|<input[^>]*\sname="{Regex.Escape(name)}"[^>]*type="hidden"[^>]*>""");
        var value = Regex.Match(input.Value, """\svalue="([^"]*)" """.TrimEnd());
        return value.Success ? WebUtility.HtmlDecode(value.Groups[1].Value) : null;
    }

    // Whether the input whose tag contains this text is ticked or chosen, whatever order its attributes are in.
    private static bool IsChecked(string html, string marker) =>
        Regex.Matches(html, "<input[^>]*>").Any(input => input.Value.Contains(marker, StringComparison.Ordinal) && input.Value.Contains(" checked", StringComparison.Ordinal));

    private static string? SelectedValue(string html, string name)
    {
        var select = Regex.Match(html, $"""<select[^>]*\sname="{Regex.Escape(name)}"[^>]*>(.*?)</select>""", RegexOptions.Singleline);
        var option = Regex.Match(select.Groups[1].Value, """<option[^>]*selected[^>]*value="([^"]*)"|<option[^>]*value="([^"]*)"[^>]*selected""");
        return option.Success ? (option.Groups[1].Success ? option.Groups[1].Value : option.Groups[2].Value) : null;
    }
}
