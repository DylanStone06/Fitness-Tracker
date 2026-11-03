using System.Net;
using System.Text.RegularExpressions;
using FitnessTracker.Domain.Workouts;
using FitnessTracker.Web.Data;
using FitnessTracker.Web.Tests.Infrastructure;
using NodaTime;

namespace FitnessTracker.Web.Tests.Workouts;

// FIT-3, Q26-Q29: a run's session page logs what the run was like once it has started: its type, the time it took,
// its distance (typed in km or miles as chosen in Preferences, stored in whole metres: Q25) and how hard it felt, each
// optional. Pace is worked out, never stored. Today in tests is Monday 19 October 2026, 13:00 in London: a run at 07:00
// today for an hour is over; one tomorrow is still to come.
public sealed partial class RunLogPageTests(TestAppFactory factory) : IClassFixture<TestAppFactory>
{
    private static readonly LocalDate Today = new(2026, 10, 19);

    [Fact]
    public async Task ARun_IsSavedAsDone_WithItsDetails_InMetresAndSeconds()
    {
        var (browser, id) = await SignedInWithASessionAsync("2026-10-19");

        var response = await LogRunAsync(browser, id, "2026-10-19", AnEasyRun);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var log = await StoredData.SessionLogAsync(factory, id, Today);
        Assert.NotNull(log);
        Assert.Equal(SessionMark.Done, log.Mark);
        Assert.Equal(RunType.Easy, log.RunType);
        Assert.Null(log.CustomRunType);
        Assert.Equal((28 * 60) + 36, log.ActualSeconds);
        Assert.Equal(5200, log.DistanceMetres);
        Assert.Equal(6, log.Effort);
    }

    // Once saved, the page shows the run's figures, its pace worked out (Q25, Q27, Q29), and the planned time beside
    // the time it took. The form is folded away under "Change the details" (P7).
    [Fact]
    public async Task ASavedRun_ShowsItsFigures_ItsPace_AndThePlannedTime()
    {
        var (browser, id) = await SignedInWithASessionAsync("2026-10-19");
        var before = await PageAsync(browser, id, "2026-10-19");
        await LogRunAsync(browser, id, "2026-10-19", AnEasyRun);

        var page = await PageAsync(browser, id, "2026-10-19");
        var text = TextOf(page);

        Assert.Contains("Log this run", TextOf(before));
        Assert.Contains("Easy run", text);
        Assert.Contains("Distance 5.2 km", text);
        Assert.Contains("Time 28:36 planned 1 h", text);
        Assert.Contains("Pace 5:30 per km", text);
        Assert.Contains("Effort 6 of 10 moderate", text);
        Assert.Contains("Change the details", text);
        Assert.DoesNotMatch(FoldedOpen(), page);
    }

    // Q25: in miles, when chosen in Preferences: typed and shown in miles, stored in metres, and the pace is per mile.
    [Fact]
    public async Task InMiles_ARunIsTypedAndShownInMiles_AndStoredInMetres()
    {
        var (browser, id) = await SignedInWithASessionAsync("2026-10-19");
        Assert.Equal(HttpStatusCode.Redirect, (await browser.SavePreferencesAsync(new() { ["DistanceUnit"] = "Miles" })).StatusCode);
        var form = TextOf(await PageAsync(browser, id, "2026-10-19"));

        await LogRunAsync(browser, id, "2026-10-19", new() { ["TimeMinutes"] = "40", ["Distance"] = "5" });
        var text = TextOf(await PageAsync(browser, id, "2026-10-19"));

        Assert.Contains("Distance (miles)", form);
        Assert.Equal(8047, (await StoredData.SessionLogAsync(factory, id, Today))!.DistanceMetres);
        Assert.Contains("Distance 5 miles", text);
        Assert.Contains("Pace 8:00 per mile", text);
    }

    // Q27: the distance box is filled so that saving it unchanged gives back the same metres: 5000 m is "3.107" miles
    // ("3.11" would save as 5005 m).
    [Fact]
    public async Task ResavingAnUnchangedRunInMiles_KeepsTheSameMetres()
    {
        var (browser, id) = await SignedInWithASessionAsync("2026-10-19");
        Assert.Equal(HttpStatusCode.Redirect, (await browser.SavePreferencesAsync(new() { ["DistanceUnit"] = "Miles" })).StatusCode);
        await LogRunAsync(browser, id, "2026-10-19", new() { ["Distance"] = "3.107" });
        var before = (await StoredData.SessionLogAsync(factory, id, Today))!.DistanceMetres;

        var shown = BoxValue(await PageAsync(browser, id, "2026-10-19"), "Distance");
        await LogRunAsync(browser, id, "2026-10-19", new() { ["Distance"] = shown });

        Assert.Equal(5000, before);
        Assert.Equal("3.107", shown);
        Assert.Equal(5000, (await StoredData.SessionLogAsync(factory, id, Today))!.DistanceMetres);
    }

    // Q26: every detail is optional: saving with none just marks it done.
    [Fact]
    public async Task ARun_CanBeSavedAsDone_WithNoDetails()
    {
        var (browser, id) = await SignedInWithASessionAsync("2026-10-19");

        var response = await LogRunAsync(browser, id, "2026-10-19", []);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var log = await StoredData.SessionLogAsync(factory, id, Today);
        Assert.NotNull(log);
        Assert.Equal(SessionMark.Done, log.Mark);
        Assert.Null(log.RunType);
        Assert.Null(log.ActualSeconds);
        Assert.Null(log.DistanceMetres);
        Assert.Null(log.Effort);
    }

    // Q27: one box of the time is enough; the blank ones count as nothing.
    [Fact]
    public async Task OneTimeBox_IsEnough()
    {
        var (browser, id) = await SignedInWithASessionAsync("2026-10-19");

        await LogRunAsync(browser, id, "2026-10-19", new() { ["TimeMinutes"] = "45" });

        Assert.Equal(45 * 60, (await StoredData.SessionLogAsync(factory, id, Today))!.ActualSeconds);
    }

    // What can't be saved is said, beside its box and in the summary at the top, and nothing is stored.
    [Theory]
    [InlineData("Distance=abc", "Enter the distance as a number, for example 5.2.")]
    [InlineData("Distance=5,2", "Enter the distance as a number, for example 5.2.")]
    [InlineData("Distance=-1", "Enter the distance as a number, for example 5.2.")]
    [InlineData("Distance=0", "Enter a distance of at least 0.01 km.")]
    [InlineData("Distance=0.001", "Enter a distance of at least 0.01 km.")]
    [InlineData("Distance=1000.5", "Enter a distance of at most 1,000 km.")]
    [InlineData("TimeHours=25", "Enter the hours as a whole number from 0 to 24.")]
    [InlineData("TimeMinutes=60", "Enter the minutes as a whole number from 0 to 59.")]
    [InlineData("TimeSeconds=60", "Enter the seconds as a whole number from 0 to 59.")]
    [InlineData("TimeHours=24&TimeMinutes=1", "Enter a time of at most 24 hours.")]
    [InlineData("TimeHours=0&TimeMinutes=0&TimeSeconds=0", "Enter how long it took, or leave the hours, minutes and seconds blank.")]
    [InlineData("Effort=0", "Choose how hard it felt from the list.")]
    [InlineData("Effort=11", "Choose how hard it felt from the list.")]
    [InlineData("RunType=Custom", "Enter a name for the custom type, or choose another type.")]
    [InlineData("RunType=Custom&CustomRunType=Hill repeats up Ditchling Beacon, twice over", "The name of the custom type must be 40 characters or fewer.")]
    public async Task WhatCantBeSaved_IsSaid_AndNothingIsStored(string boxes, string message)
    {
        var (browser, id) = await SignedInWithASessionAsync("2026-10-19");

        var response = await LogRunAsync(browser, id, "2026-10-19", Boxes(boxes));
        var page = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("There is a problem", page);
        Assert.Contains(message, page);
        Assert.Null(await StoredData.SessionLogAsync(factory, id, Today));
    }

    // ... in miles too.
    [Fact]
    public async Task TheDistancesLimits_AreSaidInMiles_WhenChosen()
    {
        var (browser, id) = await SignedInWithASessionAsync("2026-10-19");
        Assert.Equal(HttpStatusCode.Redirect, (await browser.SavePreferencesAsync(new() { ["DistanceUnit"] = "Miles" })).StatusCode);

        var response = await LogRunAsync(browser, id, "2026-10-19", new() { ["Distance"] = "1001" });

        Assert.Contains("Enter a distance of at most 1,000 miles.", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
    }

    // A done run whose changed details can't be saved has its folded form opened, to show what needs fixing.
    [Fact]
    public async Task ADoneRunsForm_IsOpened_WhenWhatWasTypedCantBeSaved()
    {
        var (browser, id) = await SignedInWithASessionAsync("2026-10-19");
        await LogRunAsync(browser, id, "2026-10-19", AnEasyRun);

        var response = await LogRunAsync(browser, id, "2026-10-19", new() { ["Distance"] = "abc" });

        Assert.Matches(FoldedOpen(), await response.Content.ReadAsStringAsync());
        Assert.Equal(5200, (await StoredData.SessionLogAsync(factory, id, Today))!.DistanceMetres);
    }

    // Q26: a custom type is saved with its name. A name typed with another type chosen isn't: its box is hidden then.
    [Fact]
    public async Task ACustomType_IsSavedWithItsName_AndANameWithAnotherTypeIsNot()
    {
        var (browser, id) = await SignedInWithASessionAsync("2026-10-19");
        var (otherBrowser, other) = await SignedInWithASessionAsync("2026-10-19");

        await LogRunAsync(browser, id, "2026-10-19", new() { ["RunType"] = "Custom", ["CustomRunType"] = "  Parkrun  " });
        await LogRunAsync(otherBrowser, other, "2026-10-19", new() { ["RunType"] = "Tempo", ["CustomRunType"] = "Parkrun" });

        var custom = await StoredData.SessionLogAsync(factory, id, Today);
        var tempo = await StoredData.SessionLogAsync(factory, other, Today);
        Assert.Equal((RunType.Custom, "Parkrun"), (custom!.RunType, custom.CustomRunType));
        Assert.Equal((RunType.Tempo, (string?)null), (tempo!.RunType, tempo.CustomRunType));
        Assert.Contains("Parkrun", TextOf(await PageAsync(browser, id, "2026-10-19")));
    }

    // Q28: marking a run skipped removes its details, and ignores the boxes, even what can't be read.
    [Fact]
    public async Task SkippingARun_RemovesItsDetails_AndIgnoresTheBoxes()
    {
        var (browser, id) = await SignedInWithASessionAsync("2026-10-19");
        await LogRunAsync(browser, id, "2026-10-19", AnEasyRun);
        var page = TextOf(await PageAsync(browser, id, "2026-10-19"));

        var response = await LogRunAsync(browser, id, "2026-10-19", new() { ["Distance"] = "abc", ["Effort"] = "99", ["TimeHours"] = "x" }, mark: "Skipped");

        Assert.Contains("Marking it skipped or back to planned removes the run's details.", page);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var log = await StoredData.SessionLogAsync(factory, id, Today);
        Assert.Equal(SessionMark.Skipped, log!.Mark);
        Assert.Equal((null, null, null, null, null), (log.RunType, log.CustomRunType, log.ActualSeconds, log.DistanceMetres, log.Effort));
    }

    // Q21: a run still to come has no form, and details posted anyway aren't saved.
    [Fact]
    public async Task ARunStillToCome_HasNoForm_AndItsDetailsArentSaved()
    {
        var (browser, id) = await SignedInWithASessionAsync("2026-10-20");
        var page = TextOf(await PageAsync(browser, id, "2026-10-20"));

        var done = await LogRunAsync(browser, id, "2026-10-20", AnEasyRun);
        var skipped = await LogRunAsync(browser, id, "2026-10-20", AnEasyRun, mark: "Skipped");

        Assert.DoesNotContain("Log this run", page);
        Assert.DoesNotContain("Distance (km)", page);
        Assert.Contains("It hasn't started yet, so it can't be marked done.", WebUtility.HtmlDecode(await done.Content.ReadAsStringAsync()));
        Assert.Equal(HttpStatusCode.Redirect, skipped.StatusCode);
        var log = await StoredData.SessionLogAsync(factory, id, Today.PlusDays(1));
        Assert.Equal((SessionMark.Skipped, (int?)null), (log!.Mark, log.DistanceMetres));
    }

    // Q20: only a run's page has the run's form (the gym's sets come with FIT-2), and run details posted to another
    // kind of session are ignored.
    [Fact]
    public async Task AGymSession_HasNoRunForm_AndIgnoresRunDetails()
    {
        var (browser, id) = await SignedInWithASessionAsync("2026-10-19", category: "Gym");
        var page = TextOf(await PageAsync(browser, id, "2026-10-19"));

        var response = await LogRunAsync(browser, id, "2026-10-19", AnEasyRun);

        Assert.DoesNotContain("Log this run", page);
        Assert.Contains("Mark as done", page);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var log = await StoredData.SessionLogAsync(factory, id, Today);
        Assert.Equal((SessionMark.Done, (int?)null, (RunType?)null), (log!.Mark, log.DistanceMetres, log.RunType));
    }

    // Requirements §9: a page opened before another tab saved the run can't overwrite its details without saying so.
    [Fact]
    public async Task APageOpenedBeforeAnotherTabSavedTheRun_CantOverwriteIt()
    {
        var (browser, id) = await SignedInWithASessionAsync("2026-10-19");
        var oldTab = await browser.LoadFormAsync($"/Sessions/Log/{id}?date=2026-10-19");
        await LogRunAsync(browser, id, "2026-10-19", AnEasyRun);

        var refused = await browser.SubmitAsync(oldTab, $"/Sessions/Log/{id}", RunForm("2026-10-19", new() { ["Distance"] = "10" }, "Done"));

        Assert.Contains("This session was changed in another tab or window after you opened this page.", WebUtility.HtmlDecode(await refused.Content.ReadAsStringAsync()));
        Assert.Equal(5200, (await StoredData.SessionLogAsync(factory, id, Today))!.DistanceMetres);
    }

    // T19: another user's run is "not found", and nothing is saved to it.
    [Fact]
    public async Task AnotherUsersRun_IsNotFound_AndNothingIsSaved()
    {
        var (_, alexs) = await SignedInWithASessionAsync("2026-10-19");
        var (sam, sams) = await SignedInWithASessionAsync("2026-10-19", user: "sam");

        var response = await sam.SubmitFormAsync($"/Sessions/Log/{sams}?date=2026-10-19", $"/Sessions/Log/{alexs}", RunForm("2026-10-19", AnEasyRun, "Done"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(await StoredData.SessionLogAsync(factory, alexs, Today));
    }

    private static Dictionary<string, string> AnEasyRun => new()
    {
        ["RunType"] = "Easy",
        ["TimeMinutes"] = "28",
        ["TimeSeconds"] = "36",
        ["Distance"] = "5.2",
        ["Effort"] = "6",
    };

    private async Task<(TestBrowser Browser, Guid Id)> SignedInWithASessionAsync(string date, string category = "Run", string user = "alex")
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, user);
        var title = TestBrowser.NewName(category);
        Assert.Equal(HttpStatusCode.Redirect, (await browser.AddActivityAsync(title, new() { ["StartDate"] = date, ["Category"] = category })).StatusCode);
        return (browser, (await StoredData.ActivityTitledAsync(factory, title))!.Id);
    }

    // Posts the session's page with every box of the run's form, as a browser sends them: blank unless given.
    private static Task<HttpResponseMessage> LogRunAsync(TestBrowser browser, Guid id, string date, Dictionary<string, string> boxes, string mark = "Done") =>
        browser.SubmitFormAsync($"/Sessions/Log/{id}?date={date}", $"/Sessions/Log/{id}", RunForm(date, boxes, mark));

    private static Dictionary<string, string> RunForm(string date, Dictionary<string, string> boxes, string mark)
    {
        var form = new Dictionary<string, string>
        {
            ["Date"] = date,
            ["Mark"] = mark,
            ["RunType"] = "",
            ["CustomRunType"] = "",
            ["TimeHours"] = "",
            ["TimeMinutes"] = "",
            ["TimeSeconds"] = "",
            ["Distance"] = "",
            ["Effort"] = "",
        };
        foreach (var (name, value) in boxes)
        {
            form[name] = value;
        }

        return form;
    }

    // "Distance=abc&Effort=0" as boxes.
    private static Dictionary<string, string> Boxes(string text) =>
        text.Split('&').Select(pair => pair.Split('=', 2)).ToDictionary(pair => pair[0], pair => pair[1]);

    private static async Task<string> PageAsync(TestBrowser browser, Guid id, string date)
    {
        var response = await browser.GetAsync($"/Sessions/Log/{id}?date={date}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    // The words of a page as it reads, without its markup: "Distance 5.2 km".
    private static string TextOf(string html) =>
        Spaces().Replace(WebUtility.HtmlDecode(Tags().Replace(html, " ")), " ");

    // What the box named name is filled with.
    private static string BoxValue(string html, string name)
    {
        var box = Regex.Match(html, $"<input[^>]*\\bname=\"{name}\"[^>]*>");
        Assert.True(box.Success, $"No {name} box on the page.");
        return WebUtility.HtmlDecode(Regex.Match(box.Value, "\\bvalue=\"([^\"]*)\"").Groups[1].Value);
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex("\\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex("<details[^>]*\\bopen\\b")]
    private static partial Regex FoldedOpen();
}
