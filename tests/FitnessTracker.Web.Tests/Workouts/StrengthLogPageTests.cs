using System.Net;
using System.Text.RegularExpressions;
using FitnessTracker.Domain.Workouts;
using FitnessTracker.Web.Data;
using FitnessTracker.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;

namespace FitnessTracker.Web.Tests.Workouts;

// FIT-2, Q30-Q33: a gym session's page logs the workout once it has started: rows of exercise, reps, weight (kg or lb,
// stored in whole grams) and rest, blank rows ignored, exercises matched by name ignoring capitals and stored for the
// user the first time. Today in tests is Monday 19 October 2026, 13:00 in London: a gym session at 07:00 today is over.
public sealed partial class StrengthLogPageTests(TestAppFactory factory) : IClassFixture<TestAppFactory>
{
    private static readonly LocalDate Today = new(2026, 10, 19);

    [Fact]
    public async Task AWorkout_IsSavedAsDone_WithItsSetsInOrder_InGrams()
    {
        var (browser, id) = await SignedInWithASessionAsync();

        var response = await LogWorkoutAsync(browser, id, AWorkout);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var (log, sets) = await StoredWorkoutAsync(id);
        Assert.Equal(SessionMark.Done, log!.Mark);
        Assert.Equal(
            [("Back squat", 5, (int?)100_000, (int?)90), ("Back squat", 5, 102_500, null), ("Pull-up", 8, null, 60)],
            sets.Select(set => (set.Exercise, set.Reps, set.WeightGrams, set.RestSeconds)));
    }

    // Q30: "back SQUAT" is the same exercise as Back squat, stored once with its first spelling; blank rows between are
    // ignored and the order kept.
    [Fact]
    public async Task ExercisesAreMatchedIgnoringCapitals_AndBlankRowsAreIgnored()
    {
        var (browser, id) = await SignedInWithASessionAsync();

        await LogWorkoutAsync(browser, id, Rows(("Back squat", "5", "100", ""), ("", "", "", ""), ("  back   SQUAT ", "5", "100", "")));

        var (_, sets) = await StoredWorkoutAsync(id);
        Assert.Equal(["Back squat", "Back squat"], sets.Select(set => set.Exercise));
        Assert.Equal([0, 1], sets.Select(set => set.Position));
        Assert.Single(await ExercisesOfAsync(id));
    }

    // Once saved, the page shows the workout: tiles (exercises, sets, weight lifted) and each exercise with its sets, rest
    // where given. The form is folded away under "Change the workout".
    [Fact]
    public async Task ASavedWorkout_ShowsItsExercisesAndSets()
    {
        var (browser, id) = await SignedInWithASessionAsync();
        var before = TextOf(await PageAsync(browser, id));
        await LogWorkoutAsync(browser, id, AWorkout);

        var page = await PageAsync(browser, id);
        var text = TextOf(page);

        Assert.Contains("Log this workout", before);
        Assert.Contains("Exercises 2", text);
        Assert.Contains("Sets 3", text);
        Assert.Contains("Weight lifted 1,012.5 kg", text); // 5 x 100 + 5 x 102.5
        Assert.Contains("Back squat 5 × 100 kg, rest 90 s 5 × 102.5 kg", text);
        Assert.Contains("Pull-up 8 reps, rest 60 s", text);
        Assert.Contains("Change the workout", text);
        Assert.DoesNotMatch(FoldedOpen(), page);
    }

    // Q33 with Q25: in pounds when chosen: typed and shown in lb, stored in grams; and saved unchanged, the same grams.
    [Fact]
    public async Task InPounds_AWorkoutIsTypedAndShownInPounds_AndResavedUnchanged()
    {
        var (browser, id) = await SignedInWithASessionAsync(weightUnit: "Pounds");
        await LogWorkoutAsync(browser, id, Rows(("Deadlift", "3", "225", "")));
        var page = await PageAsync(browser, id);

        var shown = BoxValue(page, "Sets[0].Weight");
        await LogWorkoutAsync(browser, id, Rows(("Deadlift", "3", shown, "")));

        Assert.Contains("Weight (lb)", TextOf(page));
        Assert.Contains("Deadlift 3 × 225 lb", TextOf(page));
        Assert.Equal("225", shown);
        Assert.Equal(102_058, Assert.Single((await StoredWorkoutAsync(id)).Sets).WeightGrams);
    }

    // Q30: suggestions are the starter list and the user's own exercises, never another user's (T19).
    [Fact]
    public async Task Suggestions_AreTheStarterListAndYourOwnExercises_NotAnotherUsers()
    {
        var (alex, alexs) = await SignedInWithASessionAsync();
        await LogWorkoutAsync(alex, alexs, Rows(("Sled push", "4", "", "")));
        var (sam, sams) = await SignedInWithASessionAsync(user: "sam");

        var alexsPage = await PageAsync(alex, alexs);
        var samsPage = await PageAsync(sam, sams);

        Assert.Contains("<option value=\"Sled push\">", alexsPage);
        Assert.Contains("<option value=\"Romanian deadlift\">", alexsPage);
        Assert.DoesNotContain("Sled push", samsPage);
    }

    // What can't be saved is said, naming the row, and nothing is stored.
    [Theory]
    [InlineData("|5|100|", "Row 1: enter the exercise, or clear the row.")]
    [InlineData("Back squat|||", "Row 1: enter the reps for Back squat.")]
    [InlineData("Back squat|0||", "Row 1: enter the reps as a whole number from 1 to 1,000.")]
    [InlineData("Back squat|1001||", "Row 1: enter the reps as a whole number from 1 to 1,000.")]
    [InlineData("Back squat|5|abc|", "Row 1: enter the weight as a number, for example 100 or 102.5.")]
    [InlineData("Back squat|5|100,5|", "Row 1: enter the weight as a number, for example 100 or 102.5.")]
    [InlineData("Back squat|5|1000.5|", "Row 1: enter a weight of at most 1,000 kg.")]
    [InlineData("Back squat|5||3601", "Row 1: enter the rest in seconds, from 0 to 3,600 (an hour).")]
    [InlineData("Back squat|5||-1", "Row 1: enter the rest in seconds, from 0 to 3,600 (an hour).")]
    [InlineData("Lateral raise with a pause at the top and a slow lowering pha|5||", "Row 1: the exercise's name must be 60 characters or fewer.")] // 61
    public async Task WhatCantBeSaved_IsSaid_AndNothingIsStored(string row, string message)
    {
        var (browser, id) = await SignedInWithASessionAsync();
        var parts = row.Split('|');

        var response = await LogWorkoutAsync(browser, id, Rows((parts[0], parts[1], parts[2], parts[3])));
        var page = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("There is a problem", page);
        Assert.Contains(message, page);
        Assert.Matches(FoldedOpenOrLogging(), page);
        Assert.Null(await StoredData.SessionLogAsync(factory, id, Today));
    }

    // Q31: at most 60 sets a session, even posted by hand.
    [Fact]
    public async Task MoreThanSixtySets_AreRefused()
    {
        var (browser, id) = await SignedInWithASessionAsync();
        var rows = Enumerable.Range(0, 61).Select(_ => ("Plank", "1", "", "")).ToArray();

        var response = await LogWorkoutAsync(browser, id, Rows(rows));

        Assert.Contains("Up to 60 sets can be saved for one session.", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        Assert.Null(await StoredData.SessionLogAsync(factory, id, Today));
    }

    // Q31: "Add more rows" shows six more, keeping what was typed, and saves nothing.
    [Fact]
    public async Task AddMoreRows_ShowsSixMore_KeepingWhatWasTyped()
    {
        var (browser, id) = await SignedInWithASessionAsync();
        var before = await PageAsync(browser, id);
        var form = WorkoutForm(Rows(("Back squat", "5", "abc", "")));
        form["MoreRows"] = "true";

        var response = await browser.SubmitFormAsync($"/Sessions/Log/{id}?date=2026-10-19", $"/Sessions/Log/{id}", form);
        var page = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(6, ExerciseBoxes().Matches(before).Count);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(12, ExerciseBoxes().Matches(page).Count);
        Assert.Equal("Back squat", BoxValue(page, "Sets[0].Exercise"));
        Assert.Equal("abc", BoxValue(page, "Sets[0].Weight"));
        Assert.DoesNotContain("There is a problem", page);
        Assert.Null(await StoredData.SessionLogAsync(factory, id, Today));
    }

    // Q28 and the plan: marking it skipped removes the sets; the notes stay.
    [Fact]
    public async Task Skipping_RemovesTheSets()
    {
        var (browser, id) = await SignedInWithASessionAsync();
        await LogWorkoutAsync(browser, id, AWorkout);

        var response = await LogWorkoutAsync(browser, id, AWorkout, mark: "Skipped");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var (log, sets) = await StoredWorkoutAsync(id);
        Assert.Equal(SessionMark.Skipped, log!.Mark);
        Assert.Empty(sets);
    }

    // Q20, Q21: a run has no set rows, and sets posted to it are ignored; a gym session still to come has no form.
    [Fact]
    public async Task ARunOrASessionStillToCome_HasNoSetRows_AndSetsPostedAreIgnored()
    {
        var (runBrowser, run) = await SignedInWithASessionAsync(category: "Run");
        var (laterBrowser, later) = await SignedInWithASessionAsync(date: "2026-10-20");
        var runPage = await PageAsync(runBrowser, run);
        var laterPage = await PageAsync(laterBrowser, later, "2026-10-20");

        var runSaved = await LogWorkoutAsync(runBrowser, run, AWorkout);

        Assert.DoesNotMatch(ExerciseBoxes(), runPage);
        Assert.DoesNotMatch(ExerciseBoxes(), laterPage);
        Assert.Equal(HttpStatusCode.Redirect, runSaved.StatusCode);
        Assert.Empty((await StoredWorkoutAsync(run)).Sets);
    }

    // Requirements §9: a page opened before another tab saved the workout can't replace its sets, even when only the
    // sets changed in between (the mark and notes the same).
    [Fact]
    public async Task APageOpenedBeforeAnotherTabChangedOnlyTheSets_CantReplaceThem()
    {
        var (browser, id) = await SignedInWithASessionAsync();
        await LogWorkoutAsync(browser, id, AWorkout);
        var oldTab = await browser.LoadFormAsync($"/Sessions/Log/{id}?date=2026-10-19");
        var changed = await LogWorkoutAsync(browser, id, Rows(("Deadlift", "3", "140", "")));

        var refused = await browser.SubmitAsync(oldTab, $"/Sessions/Log/{id}", WorkoutForm(Rows(("Bench press", "5", "80", ""))));
        var page = WebUtility.HtmlDecode(await refused.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Redirect, changed.StatusCode);
        Assert.True(page.Contains("This session was changed in another tab or window after you opened this page.", StringComparison.Ordinal),
            Regex.Match(page, "There is a problem.*?</ul>", RegexOptions.Singleline).Value);
        Assert.Equal(["Deadlift"], (await StoredWorkoutAsync(id)).Sets.Select(set => set.Exercise));
    }

    // T19: another user's session is "not found", and no sets are saved to it.
    [Fact]
    public async Task AnotherUsersSession_IsNotFound_AndNothingIsSaved()
    {
        var (_, alexs) = await SignedInWithASessionAsync();
        var (sam, sams) = await SignedInWithASessionAsync(user: "sam");

        var response = await sam.SubmitFormAsync($"/Sessions/Log/{sams}?date=2026-10-19", $"/Sessions/Log/{alexs}", WorkoutForm(AWorkout));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(await StoredData.SessionLogAsync(factory, alexs, Today));
    }

    private static (string Exercise, string Reps, string Weight, string RestBox)[] AWorkout =>
        Rows(("Back squat", "5", "100", "90"), ("Back squat", "5", "102.5", ""), ("Pull-up", "8", "", "60"));

    private static (string Exercise, string Reps, string Weight, string RestBox)[] Rows(params (string Exercise, string Reps, string Weight, string RestBox)[] rows) => rows;

    private async Task<(TestBrowser Browser, Guid Id)> SignedInWithASessionAsync(
        string category = "Gym", string user = "alex", string date = "2026-10-19", string weightUnit = "Kilograms")
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, user);
        Assert.Equal(HttpStatusCode.Redirect, (await browser.SavePreferencesAsync(new() { ["WeightUnit"] = weightUnit })).StatusCode);
        var title = TestBrowser.NewName(category);
        Assert.Equal(HttpStatusCode.Redirect, (await browser.AddActivityAsync(title, new() { ["StartDate"] = date, ["Category"] = category })).StatusCode);
        return (browser, (await StoredData.ActivityTitledAsync(factory, title))!.Id);
    }

    private static Task<HttpResponseMessage> LogWorkoutAsync(
        TestBrowser browser, Guid id, (string Exercise, string Reps, string Weight, string RestBox)[] rows, string mark = "Done") =>
        browser.SubmitFormAsync($"/Sessions/Log/{id}?date=2026-10-19", $"/Sessions/Log/{id}", WorkoutForm(rows, mark));

    // The page's form as a browser sends it: the mark pressed, the notes, and each row given (blank rows too).
    private static Dictionary<string, string> WorkoutForm((string Exercise, string Reps, string Weight, string RestBox)[] rows, string mark = "Done")
    {
        var form = new Dictionary<string, string> { ["Date"] = "2026-10-19", ["Mark"] = mark, ["Notes"] = "" };
        for (var i = 0; i < rows.Length; i++)
        {
            form[$"Sets[{i}].Exercise"] = rows[i].Exercise;
            form[$"Sets[{i}].Reps"] = rows[i].Reps;
            form[$"Sets[{i}].Weight"] = rows[i].Weight;
            form[$"Sets[{i}].Rest"] = rows[i].RestBox;
        }

        return form;
    }

    // The session's log and its sets in order, with each set's exercise name, read past the per-user filter.
    private async Task<(SessionLog? Log, List<(string Exercise, int Position, int Reps, int? WeightGrams, int? RestSeconds)> Sets)> StoredWorkoutAsync(Guid activity)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var log = await db.SessionLogs.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(log => log.ActivityId == activity && log.OccurrenceDate == Today);
        var sets = log is null ? [] : await db.SessionSets.IgnoreQueryFilters().AsNoTracking()
            .Where(set => set.SessionLogId == log.Id)
            .OrderBy(set => set.Position)
            .Join(db.Exercises.IgnoreQueryFilters(), set => set.ExerciseId, exercise => exercise.Id,
                (set, exercise) => new { exercise.Name, set.Position, set.Reps, set.WeightGrams, set.RestSeconds })
            .ToListAsync();
        return (log, sets.Select(set => (set.Name, set.Position, set.Reps, set.WeightGrams, set.RestSeconds)).ToList());
    }

    // The exercises of the user who owns this activity.
    private async Task<List<Exercise>> ExercisesOfAsync(Guid activity)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var owner = await db.Activities.IgnoreQueryFilters().Where(a => a.Id == activity).Select(a => a.OwnerId).SingleAsync();
        return await db.Exercises.IgnoreQueryFilters().AsNoTracking().Where(exercise => exercise.OwnerId == owner).ToListAsync();
    }

    private static async Task<string> PageAsync(TestBrowser browser, Guid id, string date = "2026-10-19")
    {
        var response = await browser.GetAsync($"/Sessions/Log/{id}?date={date}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    // The words of a page as it reads: a place a word may break (<wbr>) is nothing; any other tag is a space.
    private static string TextOf(string html) =>
        Spaces().Replace(WebUtility.HtmlDecode(Tags().Replace(BreakHints().Replace(html, ""), " ")), " ");

    private static string BoxValue(string html, string name)
    {
        var box = Regex.Match(html, $"<input[^>]*\\bname=\"{Regex.Escape(name)}\"[^>]*>");
        Assert.True(box.Success, $"No {name} box on the page.");
        return WebUtility.HtmlDecode(Regex.Match(box.Value, "\\bvalue=\"([^\"]*)\"").Groups[1].Value);
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex("<wbr\\s*/?>")]
    private static partial Regex BreakHints();

    [GeneratedRegex("\\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex("<details[^>]*\\bopen\\b")]
    private static partial Regex FoldedOpen();

    // The form shown: either still "Log this workout" (nothing saved yet) or the fold opened.
    [GeneratedRegex("Log this workout|<details[^>]*\\bopen\\b")]
    private static partial Regex FoldedOpenOrLogging();

    [GeneratedRegex("name=\"Sets\\[\\d+\\]\\.Exercise\"")]
    private static partial Regex ExerciseBoxes();
}
