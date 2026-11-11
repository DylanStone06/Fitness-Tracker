using System.Net;
using System.Text.RegularExpressions;
using FitnessTracker.Domain.Workouts;
using FitnessTracker.Web.Data;
using FitnessTracker.Web.Tests.Infrastructure;
using NodaTime;

namespace FitnessTracker.Web.Tests.Workouts;

// FIT-4, Q34-Q36: an Other exercise session's page logs what it was once it has started: its type (walk, cycle, swim,
// mobility or custom), the time it took, an optional distance and how hard it felt (1-10), each optional, as a run's
// page does, without pace. Today in tests is Monday 19 October 2026, 13:00 in London: a session at 07:00 today is over.
public sealed partial class OtherActivityPageTests(TestAppFactory factory) : IClassFixture<TestAppFactory>
{
    private static readonly LocalDate Today = new(2026, 10, 19);

    [Fact]
    public async Task AnActivity_IsSavedAsDone_WithItsDetails_InMetresAndSeconds()
    {
        var (browser, id) = await SignedInWithASessionAsync();

        var response = await LogAsync(browser, id, ASwim);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var log = await StoredData.SessionLogAsync(factory, id, Today);
        Assert.Equal((SessionMark.Done, OtherType.Swim, (string?)null), (log!.Mark, log.OtherType, log.CustomOtherType));
        Assert.Equal((45 * 60, 1500, 7), (log.ActualSeconds, log.DistanceMetres, log.Effort));
    }

    // Saved, the page shows a card headed by its type with its figures (no pace: Q36), the form folded away.
    [Fact]
    public async Task ASavedActivity_ShowsItsTypeAndFigures_WithoutPace()
    {
        var (browser, id) = await SignedInWithASessionAsync();
        var before = TextOf(await PageAsync(browser, id));
        await LogAsync(browser, id, ASwim);

        var text = TextOf(await PageAsync(browser, id));

        Assert.Contains("Log this activity", before);
        Assert.Contains("Swim Distance 1.5 km Time 45:00 planned 1 h Effort 7 of 10 hard", text);
        Assert.DoesNotContain("Pace", text);
        Assert.Contains("Change the details", text);
    }

    // Q36: every detail is optional: saving with none just marks it done; with no type the card says "Your session".
    [Fact]
    public async Task AnActivity_CanBeSavedAsDone_WithNoDetails_OrJustATime()
    {
        var (browser, id) = await SignedInWithASessionAsync();
        var (otherBrowser, other) = await SignedInWithASessionAsync();

        await LogAsync(browser, id, []);
        await LogAsync(otherBrowser, other, new() { ["TimeMinutes"] = "30" });

        var log = await StoredData.SessionLogAsync(factory, id, Today);
        Assert.Equal((SessionMark.Done, (OtherType?)null, (int?)null), (log!.Mark, log.OtherType, log.ActualSeconds));
        Assert.Contains("Your session", TextOf(await PageAsync(otherBrowser, other)));
    }

    // Q34: a custom type is saved with its name; a name typed with another type chosen isn't (its box is hidden then).
    [Fact]
    public async Task ACustomType_IsSavedWithItsName_AndANameWithAnotherTypeIsNot()
    {
        var (browser, id) = await SignedInWithASessionAsync();
        var (otherBrowser, other) = await SignedInWithASessionAsync();

        await LogAsync(browser, id, new() { ["OtherType"] = "Custom", ["CustomOtherType"] = "  Yoga  " });
        await LogAsync(otherBrowser, other, new() { ["OtherType"] = "Walk", ["CustomOtherType"] = "Yoga" });

        var custom = await StoredData.SessionLogAsync(factory, id, Today);
        var walk = await StoredData.SessionLogAsync(factory, other, Today);
        Assert.Equal((OtherType.Custom, "Yoga"), (custom!.OtherType, custom.CustomOtherType));
        Assert.Equal((OtherType.Walk, (string?)null), (walk!.OtherType, walk.CustomOtherType));
        Assert.Contains("Yoga", TextOf(await PageAsync(browser, id)));
    }

    // The checks a run's details have apply here too, and nothing is stored.
    [Theory]
    [InlineData("OtherType=Custom", "Enter a name for the custom type, or choose another type.")]
    [InlineData("Distance=abc", "Enter the distance as a number, for example 5.2.")]
    [InlineData("TimeMinutes=60", "Enter the minutes as a whole number from 0 to 59.")]
    [InlineData("Effort=11", "Choose how hard it felt from the list.")]
    public async Task WhatCantBeSaved_IsSaid_AndNothingIsStored(string box, string message)
    {
        var (browser, id) = await SignedInWithASessionAsync();
        var (name, value) = (box.Split('=')[0], box.Split('=')[1]);

        var response = await LogAsync(browser, id, new() { [name] = value });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(message, WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        Assert.Null(await StoredData.SessionLogAsync(factory, id, Today));
    }

    // Q28: marking it skipped removes its details; the notes stay.
    [Fact]
    public async Task Skipping_RemovesTheDetails_AndKeepsTheNotes()
    {
        var (browser, id) = await SignedInWithASessionAsync();
        await LogAsync(browser, id, ASwim);
        var page = TextOf(await PageAsync(browser, id));

        await LogAsync(browser, id, new() { ["Notes"] = "Pool closed" }, mark: "Skipped");

        Assert.Contains("Marking it skipped or back to planned removes its details.", page);
        var log = await StoredData.SessionLogAsync(factory, id, Today);
        Assert.Equal((SessionMark.Skipped, "Pool closed"), (log!.Mark, log.Notes));
        Assert.Equal(((OtherType?)null, (int?)null, (int?)null, (int?)null), (log.OtherType, log.ActualSeconds, log.DistanceMetres, log.Effort));
    }

    // Q20: only an Other exercise page has these boxes; posted to a run, the type is ignored. Q21: none before it starts.
    [Fact]
    public async Task OtherPagesAndSessionsStillToCome_HaveNoTypeOfActivity_AndItIsIgnored()
    {
        var (runBrowser, run) = await SignedInWithASessionAsync(category: "Run");
        var (laterBrowser, later) = await SignedInWithASessionAsync(date: "2026-10-20");
        var runPage = TextOf(await PageAsync(runBrowser, run));
        var laterPage = TextOf(await PageAsync(laterBrowser, later, "2026-10-20"));

        await LogAsync(runBrowser, run, ASwim);

        Assert.DoesNotContain("Type of activity", runPage);
        Assert.DoesNotContain("Type of activity", laterPage);
        Assert.Null((await StoredData.SessionLogAsync(factory, run, Today))!.OtherType);
    }

    // Requirements §9: a page opened before another tab saved the activity can't overwrite it without saying so.
    [Fact]
    public async Task APageOpenedBeforeAnotherTabSavedIt_CantOverwriteIt()
    {
        var (browser, id) = await SignedInWithASessionAsync();
        var oldTab = await browser.LoadFormAsync($"/Sessions/Log/{id}?date=2026-10-19");
        await LogAsync(browser, id, ASwim);

        var refused = await browser.SubmitAsync(oldTab, $"/Sessions/Log/{id}", Form(new() { ["OtherType"] = "Walk" }, "Done"));

        Assert.Contains("This session was changed in another tab or window after you opened this page.", WebUtility.HtmlDecode(await refused.Content.ReadAsStringAsync()));
        Assert.Equal(OtherType.Swim, (await StoredData.SessionLogAsync(factory, id, Today))!.OtherType);
    }

    // T19: another user's session is "not found", and nothing is saved to it.
    [Fact]
    public async Task AnotherUsersSession_IsNotFound_AndNothingIsSaved()
    {
        var (_, alexs) = await SignedInWithASessionAsync();
        var (sam, sams) = await SignedInWithASessionAsync(user: "sam");

        var response = await sam.SubmitFormAsync($"/Sessions/Log/{sams}?date=2026-10-19", $"/Sessions/Log/{alexs}", Form(ASwim, "Done"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(await StoredData.SessionLogAsync(factory, alexs, Today));
    }

    private static Dictionary<string, string> ASwim => new()
    {
        ["OtherType"] = "Swim",
        ["TimeMinutes"] = "45",
        ["Distance"] = "1.5",
        ["Effort"] = "7",
    };

    private async Task<(TestBrowser Browser, Guid Id)> SignedInWithASessionAsync(string category = "OtherExercise", string user = "alex", string date = "2026-10-19")
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, user);
        Assert.Equal(HttpStatusCode.Redirect, (await browser.SavePreferencesAsync(new() { ["DistanceUnit"] = "Kilometres" })).StatusCode);
        var title = TestBrowser.NewName(category);
        Assert.Equal(HttpStatusCode.Redirect, (await browser.AddActivityAsync(title, new() { ["StartDate"] = date, ["Category"] = category })).StatusCode);
        return (browser, (await StoredData.ActivityTitledAsync(factory, title))!.Id);
    }

    private static Task<HttpResponseMessage> LogAsync(TestBrowser browser, Guid id, Dictionary<string, string> boxes, string mark = "Done") =>
        browser.SubmitFormAsync($"/Sessions/Log/{id}?date=2026-10-19", $"/Sessions/Log/{id}", Form(boxes, mark));

    // Every box of the page's form, as a browser sends them: blank unless given.
    private static Dictionary<string, string> Form(Dictionary<string, string> boxes, string mark)
    {
        var form = new Dictionary<string, string>
        {
            ["Date"] = "2026-10-19",
            ["Mark"] = mark,
            ["OtherType"] = "",
            ["CustomOtherType"] = "",
            ["TimeHours"] = "",
            ["TimeMinutes"] = "",
            ["TimeSeconds"] = "",
            ["Distance"] = "",
            ["Effort"] = "",
            ["Notes"] = "",
        };
        foreach (var (name, value) in boxes)
        {
            form[name] = value;
        }

        return form;
    }

    private static async Task<string> PageAsync(TestBrowser browser, Guid id, string date = "2026-10-19")
    {
        var response = await browser.GetAsync($"/Sessions/Log/{id}?date={date}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    // The words of a page as it reads: a place a word may break (<wbr>) is nothing; any other tag is a space.
    private static string TextOf(string html) =>
        Spaces().Replace(WebUtility.HtmlDecode(Tags().Replace(BreakHints().Replace(html, ""), " ")), " ");

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex("<wbr\\s*/?>")]
    private static partial Regex BreakHints();

    [GeneratedRegex("\\s+")]
    private static partial Regex Spaces();
}
