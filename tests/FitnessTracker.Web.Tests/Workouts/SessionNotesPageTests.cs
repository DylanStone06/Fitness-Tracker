using System.Net;
using FitnessTracker.Domain.Workouts;
using FitnessTracker.Web.Data;
using FitnessTracker.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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

    // Requirements §9 (code review of Phase 7): notes saved from a page opened before another tab changed them are
    // refused, and the page says what they are now, so they aren't replaced unseen. What was typed is still in the box.
    [Fact]
    public async Task APageOpenedBeforeAnotherTabChangedTheNotes_SaysWhatTheyAreNow()
    {
        var (browser, id) = await SignedInWithASessionAsync("Gym");
        await MarkAsync(browser, id, "Done", "");
        var oldTab = await browser.LoadFormAsync($"/Sessions/Log/{id}?date=2026-10-19");
        await MarkAsync(browser, id, "Done", "Knee sore");

        var refused = await browser.SubmitAsync(oldTab, $"/Sessions/Log/{id}", new() { ["Date"] = "2026-10-19", ["Mark"] = "Done", ["Notes"] = "Felt fine" });
        var page = WebUtility.HtmlDecode(await refused.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
        Assert.Contains(
            "This session was changed in another tab or window after you opened this page. It's now marked done, with the notes \"Knee sore\". " +
            "What you typed is still below: check it, then make your change again.", page);
        Assert.Matches(">\\s*Felt fine</textarea>", page);
        Assert.Equal("Knee sore", (await StoredData.SessionLogAsync(factory, id, Today))!.Notes);
    }

    // Code review of Phase 7: a save that fails for any other reason isn't blamed on another tab, which would only have
    // the user try again, and again. It is a fault, for the error page, and nothing is saved. (The notes are just how
    // the test makes the database refuse it: a trigger made for these notes alone.)
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ASaveThatFailsForAnotherReason_IsAFault_NotAnotherTabs(bool alreadyMarked)
    {
        var (browser, id) = await SignedInWithASessionAsync("Gym");
        if (alreadyMarked)
        {
            await MarkAsync(browser, id, "Done", "Felt fine");
        }

        await RefuseFaultNotesAsync();

        var response = await MarkAsync(browser, id, "Done", "A fault made by a test");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("another tab", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        Assert.Equal(alreadyMarked ? "Felt fine" : null, (await StoredData.SessionLogAsync(factory, id, Today))?.Notes);
    }

    // Makes the database refuse to add or change a log with the notes "A fault made by a test", as it would refuse any
    // save after a fault (a full disk, say).
    private async Task RefuseFaultNotesAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER IF NOT EXISTS RefuseFaultNotesAdded BEFORE INSERT ON SessionLogs " +
            "WHEN NEW.Notes = 'A fault made by a test' BEGIN SELECT RAISE(ABORT, 'A fault made by a test'); END;");
        await db.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER IF NOT EXISTS RefuseFaultNotesChanged BEFORE UPDATE ON SessionLogs " +
            "WHEN NEW.Notes = 'A fault made by a test' BEGIN SELECT RAISE(ABORT, 'A fault made by a test'); END;");
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
