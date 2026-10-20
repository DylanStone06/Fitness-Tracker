using System.Net;
using System.Text.Json;
using FitnessTracker.Web.Data;
using FitnessTracker.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FitnessTracker.Web.Tests.Activities;

// CAL-4: one-off changes to a repeating activity, stored with it. "Gym" is on Mondays and Wednesdays at 07:00 for an
// hour, from Monday 2 November 2026 (GMT). The changes are written straight to the database here; the pages that make
// them are tested on their own.
public sealed class OccurrenceChangesTests(TestAppFactory factory) : IClassFixture<TestAppFactory>
{
    private const string CancelWednesdayMoveNextMonday =
        """[{"date":"2026-11-04","cancelled":true},{"date":"2026-11-09","start":"2026-11-09T18:30","minutes":45}]""";

    [Fact]
    public async Task TheCalendar_LeavesOutACancelledOccurrence_AndShowsAMovedOne()
    {
        var (browser, title) = await GymWithChangesAsync(CancelWednesdayMoveNextMonday);

        var events = await EventsAsync(browser, "2026-11-02", "2026-11-12");

        Assert.Equal(
            [("2026-11-02T07:00:00", "2026-11-02T08:00:00"), ("2026-11-09T18:30:00", "2026-11-09T19:15:00"), ("2026-11-11T07:00:00", "2026-11-11T08:00:00")],
            events.Where(item => item.GetProperty("title").GetString() == title)
                .Select(item => (item.GetProperty("start").GetString(), item.GetProperty("end").GetString())));
    }

    // T8: editing the whole series keeps the changes on dates it still falls on, and says so.
    [Fact]
    public async Task EditingTheSeries_KeepsItsChanges_AndSaysHowMany()
    {
        var (browser, title) = await GymWithChangesAsync(CancelWednesdayMoveNextMonday);

        var response = await EditAsync(browser, title, new() { ["StartTime"] = "07:30" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(CancelWednesdayMoveNextMonday, (await StoredData.ActivityTitledAsync(factory, title))!.OccurrenceChanges);
        Assert.Contains("2 occurrences you changed on their own keep their changes", await ListAsync(browser));
    }

    // T8: a change on a day the edited series no longer falls on (Wednesdays dropped) is removed, and that is said too.
    [Fact]
    public async Task EditingTheSeries_RemovesChangesOnDaysItNoLongerFallsOn()
    {
        var (browser, title) = await GymWithChangesAsync(CancelWednesdayMoveNextMonday);

        await EditAsync(browser, title, new() { ["RepeatDays[2]"] = "false" });

        Assert.Equal(
            """[{"date":"2026-11-09","start":"2026-11-09T18:30","minutes":45}]""",
            (await StoredData.ActivityTitledAsync(factory, title))!.OccurrenceChanges);
        var list = await ListAsync(browser);
        Assert.Contains("1 occurrence you changed on its own keeps its change", list);
        Assert.Contains("1 change was on a day it no longer happens, so it was removed", list);
    }

    [Fact]
    public async Task MakingItAOneOff_RemovesTheChanges()
    {
        var (browser, title) = await GymWithChangesAsync(CancelWednesdayMoveNextMonday);

        await EditAsync(browser, title, new() { ["Repeats"] = "Never" });

        Assert.Null((await StoredData.ActivityTitledAsync(factory, title))!.OccurrenceChanges);
        Assert.Contains("2 changes were on days it no longer happens, so they were removed", await ListAsync(browser));
    }

    // Changes the app didn't write (edited by hand) make the activity unreadable, named rather than half-shown (P1).
    [Fact]
    public async Task ChangesThatCantBeRead_AreNamed()
    {
        var (browser, title) = await GymWithChangesAsync("""[{"date":"2026-11-05","cancelled":true}]""");

        var html = await (await browser.GetAsync("/Calendar")).Content.ReadAsStringAsync();

        Assert.Contains($"{title} couldn&#x27;t be read", html);
    }

    private async Task<(TestBrowser Browser, string Title)> GymWithChangesAsync(string changes)
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var title = TestBrowser.NewName("Gym");
        await browser.AddActivityAsync(title, GymForm());
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Activities.IgnoreQueryFilters()
            .Where(activity => activity.Title == title)
            .ExecuteUpdateAsync(set => set.SetProperty(activity => activity.OccurrenceChanges, changes));
        return (browser, title);
    }

    private static Dictionary<string, string> GymForm() => new()
    {
        ["StartDate"] = "2026-11-02",
        ["StartTime"] = "07:00",
        ["Repeats"] = "Weekly",
        ["RepeatDays[0]"] = "true",
        ["RepeatDays[2]"] = "true",
    };

    private async Task<HttpResponseMessage> EditAsync(TestBrowser browser, string title, Dictionary<string, string> changes)
    {
        var id = (await StoredData.ActivityTitledAsync(factory, title))!.Id;
        var form = TestBrowser.ActivityForm(title, GymForm());
        foreach (var (name, value) in changes)
        {
            form[name] = value;
        }

        return await browser.SubmitFormAsync($"/Activities/Edit/{id}", $"/Activities/Edit/{id}", form);
    }

    private static async Task<string> ListAsync(TestBrowser browser) =>
        await (await browser.GetAsync("/Activities")).Content.ReadAsStringAsync();

    private static async Task<List<JsonElement>> EventsAsync(TestBrowser browser, string start, string end)
    {
        var response = await browser.GetAsync($"/Calendar/Events?start={start}&end={end}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.EnumerateArray().Select(item => item.Clone()).ToList();
    }
}
