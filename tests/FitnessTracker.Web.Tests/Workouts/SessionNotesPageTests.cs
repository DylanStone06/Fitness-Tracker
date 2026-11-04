using System.Net;
using FitnessTracker.Domain.Workouts;
using FitnessTracker.Web.Tests.Infrastructure;
using NodaTime;

namespace FitnessTracker.Web.Tests.Workouts;

// FIT-2: notes on any session ("knee a bit sore"), saved with its mark: kept when it is marked skipped, removed with
// it when it is put back to planned, and changeable once marked without changing the mark. Today in tests is Monday 19
// October 2026, 13:00 in London: a session at 07:00 today is over.
public sealed class SessionNotesPageTests(TestAppFactory factory) : IClassFixture<TestAppFactory>
{
    private static readonly LocalDate Today = new(2026, 10, 19);

    [Fact]
    public async Task Notes_AreSavedWithTheMark_Trimmed()
    {
        var (browser, id) = await SignedInWithASessionAsync("Gym");

        var response = await MarkAsync(browser, id, "Done", "  Felt strong today.  ");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("Felt strong today.", (await StoredData.SessionLogAsync(factory, id, Today))!.Notes);
        Assert.Contains("Felt strong today.", await PageAsync(browser, id));
    }

    // Q28 and the plan: skipping a run removes its details but keeps the notes, which often say why.
    [Fact]
    public async Task Skipping_KeepsTheNotes_AndRemovesARunsDetails()
    {
        var (browser, id) = await SignedInWithASessionAsync("Run");
        await MarkAsync(browser, id, "Done", "Windy", new() { ["Distance"] = "5" });

        var response = await MarkAsync(browser, id, "Skipped", "Knee sore, skipped");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var log = await StoredData.SessionLogAsync(factory, id, Today);
        Assert.Equal((SessionMark.Skipped, "Knee sore, skipped", (int?)null), (log!.Mark, log.Notes, log.DistanceMetres));
    }

    // Back to planned removes the session's log, notes and all; notes posted with it don't stop it, however long.
    [Fact]
    public async Task BackToPlanned_RemovesTheNotes()
    {
        var (browser, id) = await SignedInWithASessionAsync("Gym");
        await MarkAsync(browser, id, "Done", "Felt strong");

        var response = await MarkAsync(browser, id, "Planned", new string('x', 2001));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Null(await StoredData.SessionLogAsync(factory, id, Today));
    }

    // Once marked, "Save notes" changes them and keeps the mark.
    [Theory]
    [InlineData("Done")]
    [InlineData("Skipped")]
    public async Task AMarkedSessionsNotes_CanBeChanged_KeepingTheMark(string mark)
    {
        var (browser, id) = await SignedInWithASessionAsync("Gym");
        await MarkAsync(browser, id, mark, "First thoughts");
        var page = await PageAsync(browser, id);

        var response = await MarkAsync(browser, id, mark, "Second thoughts");

        Assert.Contains($"name=\"Mark\" value=\"{mark}\" class=\"btn btn-outline-primary\">Save notes</button>", page);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var log = await StoredData.SessionLogAsync(factory, id, Today);
        Assert.Equal((Enum.Parse<SessionMark>(mark), "Second thoughts"), (log!.Mark, log.Notes));
    }

    // Blank notes are none.
    [Fact]
    public async Task BlankNotes_AreNone()
    {
        var (browser, id) = await SignedInWithASessionAsync("Gym");

        await MarkAsync(browser, id, "Done", "   ");

        Assert.Null((await StoredData.SessionLogAsync(factory, id, Today))!.Notes);
    }

    [Fact]
    public async Task NotesTooLong_AreSaid_AndNothingIsSaved()
    {
        var (browser, id) = await SignedInWithASessionAsync("Gym");

        var response = await MarkAsync(browser, id, "Done", new string('x', 2001));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Notes must be 2000 characters or fewer.", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        Assert.Null(await StoredData.SessionLogAsync(factory, id, Today));
    }

    private async Task<(TestBrowser Browser, Guid Id)> SignedInWithASessionAsync(string category)
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var title = TestBrowser.NewName(category);
        Assert.Equal(HttpStatusCode.Redirect, (await browser.AddActivityAsync(title, new() { ["StartDate"] = "2026-10-19", ["Category"] = category })).StatusCode);
        return (browser, (await StoredData.ActivityTitledAsync(factory, title))!.Id);
    }

    // Posts the session's page as a browser does: the mark pressed, the notes box, and any other boxes given.
    private static Task<HttpResponseMessage> MarkAsync(TestBrowser browser, Guid id, string mark, string notes, Dictionary<string, string>? boxes = null)
    {
        var form = new Dictionary<string, string>(boxes ?? []) { ["Date"] = "2026-10-19", ["Mark"] = mark, ["Notes"] = notes };
        return browser.SubmitFormAsync($"/Sessions/Log/{id}?date=2026-10-19", $"/Sessions/Log/{id}", form);
    }

    private static async Task<string> PageAsync(TestBrowser browser, Guid id)
    {
        var response = await browser.GetAsync($"/Sessions/Log/{id}?date=2026-10-19");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }
}
