using System.Net;
using FitnessTracker.Web.Tests.Infrastructure;

namespace FitnessTracker.Web.Tests.Routines;

// CAL-9: "create, duplicate, rename". A duplicate is a new routine with the same weeks, not the usual one and with no
// dates chosen, holding copies of the original's activities in the same weeks, without changes made to single
// occurrences. Activities in every week aren't in any routine, so they aren't copied. Today in tests is Monday 19
// October 2026; the usual routine below takes turns over two weeks, week A this week.
public sealed class DuplicateRoutineTests(TestAppFactory factory) : IClassFixture<TestAppFactory>
{
    [Fact]
    public async Task Duplicate_CopiesTheRoutineAndItsActivities_InTheSameWeeks()
    {
        var (browser, usual) = await SignedInWithABRoutineAsync();
        var gym = TestBrowser.NewName("Gym");
        var run = TestBrowser.NewName("Run");
        var work = TestBrowser.NewName("Work");
        await AddMondaysAsync(browser, gym, $"{usual.Id}/A");
        await AddMondaysAsync(browser, run, usual.Id.ToString(), "18:00");
        await AddMondaysAsync(browser, work, "", "09:00");
        var copyName = TestBrowser.NewName("Holiday");

        var response = await browser.SubmitFormAsync($"/Routines/Duplicate/{usual.Id}", $"/Routines/Duplicate/{usual.Id}", new() { ["Name"] = copyName });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var copy = (await StoredData.RoutineNamedAsync(factory, copyName))!;
        Assert.Equal((2, new NodaTime.LocalDate(2026, 10, 19), false), (copy.CycleWeeks, copy.WeekAStarts, copy.IsUsual));
        var copies = await StoredData.ActivitiesInRoutineAsync(factory, copy.Id);
        Assert.Equal([(gym, (char?)'A'), (run, null)], copies.Select(activity => (activity.Title, activity.RoutineWeek)).OrderBy(pair => pair.Title, StringComparer.Ordinal));
        Assert.Equal(2, (await StoredData.ActivitiesInRoutineAsync(factory, usual.Id)).Count);
        Assert.Empty(await StoredData.DatesChosenForAsync(factory, copy.Id));
        Assert.DoesNotContain(copies, activity => activity.Title == work);
    }

    // A copy keeps everything else about the activity (place, length, getting ready, demanding, goal...).
    [Fact]
    public async Task Duplicate_CopiesEachActivitysDetails()
    {
        var (browser, usual) = await SignedInWithABRoutineAsync();
        var gym = TestBrowser.NewName("Gym");
        await browser.AddActivityAsync(gym, new()
        {
            ["StartDate"] = "2026-10-19",
            ["StartTime"] = "07:15",
            ["DurationHours"] = "1",
            ["DurationMinutes"] = "20",
            ["Repeats"] = "Weekly",
            ["RepeatDays[0]"] = "true",
            ["RepeatDays[2]"] = "true",
            ["PreparationMinutes"] = "10",
            ["ChangingMinutes"] = "15",
            ["IsDemanding"] = "true",
            ["Notes"] = "Leg day",
            ["Weeks"] = usual.Id.ToString(),
        });
        var copyName = TestBrowser.NewName("Holiday");

        await browser.SubmitFormAsync($"/Routines/Duplicate/{usual.Id}", $"/Routines/Duplicate/{usual.Id}", new() { ["Name"] = copyName });

        var original = Assert.Single(await StoredData.ActivitiesInRoutineAsync(factory, usual.Id));
        var copy = Assert.Single(await StoredData.ActivitiesInRoutineAsync(factory, (await StoredData.RoutineNamedAsync(factory, copyName))!.Id));
        Assert.NotEqual(original.Id, copy.Id);
        Assert.Equal(
            (original.Title, original.Category, original.StartDate, original.StartTime, original.LengthMinutes, original.RepeatRule, original.TimeZoneId),
            (copy.Title, copy.Category, copy.StartDate, copy.StartTime, copy.LengthMinutes, copy.RepeatRule, copy.TimeZoneId));
        Assert.Equal(
            (original.PreparationMinutes, original.ChangingMinutes, original.IsDemanding, original.Notes, original.IsFixed, original.Priority, original.LocationId, original.GoalId),
            (copy.PreparationMinutes, copy.ChangingMinutes, copy.IsDemanding, copy.Notes, copy.IsFixed, copy.Priority, copy.LocationId, copy.GoalId));
    }

    // Changes to single occurrences are on dates in the original's weeks; the copy starts without them.
    [Fact]
    public async Task Duplicate_LeavesOutChangesToSingleOccurrences()
    {
        var (browser, usual) = await SignedInWithABRoutineAsync();
        var gym = TestBrowser.NewName("Gym");
        await AddMondaysAsync(browser, gym, usual.Id.ToString());
        var id = Assert.Single(await StoredData.ActivitiesInRoutineAsync(factory, usual.Id)).Id;
        await browser.SubmitFormAsync($"/Activities/Occurrence/{id}?date=2026-10-26", $"/Activities/Occurrence/{id}",
            new() { ["Date"] = "2026-10-26", ["Change"] = "Cancel" });
        var copyName = TestBrowser.NewName("Holiday");

        await browser.SubmitFormAsync($"/Routines/Duplicate/{usual.Id}", $"/Routines/Duplicate/{usual.Id}", new() { ["Name"] = copyName });

        var copy = Assert.Single(await StoredData.ActivitiesInRoutineAsync(factory, (await StoredData.RoutineNamedAsync(factory, copyName))!.Id));
        Assert.Null(copy.OccurrenceChanges);
        Assert.NotNull(Assert.Single(await StoredData.ActivitiesInRoutineAsync(factory, usual.Id)).OccurrenceChanges);
    }

    // The page offers a name and says what will be copied, and the list says what was done.
    [Fact]
    public async Task ThePage_OffersAName_AndSaysWhatIsCopied()
    {
        var (browser, usual) = await SignedInWithABRoutineAsync();
        await AddMondaysAsync(browser, TestBrowser.NewName("Gym"), usual.Id.ToString());
        await AddMondaysAsync(browser, TestBrowser.NewName("Run"), usual.Id.ToString(), "18:00");

        var page = WebUtility.HtmlDecode(await (await browser.GetAsync($"/Routines/Duplicate/{usual.Id}")).Content.ReadAsStringAsync());
        await browser.SubmitFormAsync($"/Routines/Duplicate/{usual.Id}", $"/Routines/Duplicate/{usual.Id}", new() { ["Name"] = $"{usual.Name} copy" });
        var list = WebUtility.HtmlDecode(await (await browser.GetAsync("/Routines")).Content.ReadAsStringAsync());

        Assert.Contains($"value=\"{usual.Name} copy\"", page);
        Assert.Contains("Its 2 activities are copied, in the same weeks, without changes you made to single occurrences.", page);
        Assert.Contains($"{usual.Name} copy was added, with copies of its 2 activities. They happen once you choose dates for it, or make it your usual routine.", list);
        Assert.Contains($"href=\"/Routines/Duplicate/{usual.Id}\"", list);
    }

    [Theory]
    [InlineData("", "Enter a name for the routine.")]
    [InlineData("SAME", "You already have a routine called")]
    public async Task Duplicate_RefusesAMissingOrUsedName(string name, string message)
    {
        var (browser, usual) = await SignedInWithABRoutineAsync();
        var before = (await StoredData.ActivitiesInRoutineAsync(factory, usual.Id)).Count;

        var response = await browser.SubmitFormAsync($"/Routines/Duplicate/{usual.Id}", $"/Routines/Duplicate/{usual.Id}",
            new() { ["Name"] = name == "SAME" ? usual.Name.ToUpperInvariant() : name });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(message, WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        Assert.Equal(before, (await StoredData.ActivitiesInRoutineAsync(factory, usual.Id)).Count);
    }

    // A two-week usual routine with week A this week (19 October 2026).
    private async Task<(TestBrowser Browser, (Guid Id, string Name) Usual)> SignedInWithABRoutineAsync()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var name = TestBrowser.NewName("Usual week");
        Assert.Equal(HttpStatusCode.Redirect, (await browser.AddRoutineAsync(name, new() { ["CycleWeeks"] = "2", ["IsUsual"] = "true" })).StatusCode);
        return (browser, ((await StoredData.RoutineNamedAsync(factory, name))!.Id, name));
    }

    // Weekly on Mondays from today, for an hour, in the weeks given ("" for every week).
    private static async Task AddMondaysAsync(TestBrowser browser, string title, string weeks, string time = "07:00")
    {
        var added = await browser.AddActivityAsync(title, new()
        {
            ["StartDate"] = "2026-10-19",
            ["StartTime"] = time,
            ["Repeats"] = "Weekly",
            ["RepeatDays[0]"] = "true",
            ["Weeks"] = weeks,
        });
        Assert.Equal(HttpStatusCode.Redirect, added.StatusCode);
    }
}
