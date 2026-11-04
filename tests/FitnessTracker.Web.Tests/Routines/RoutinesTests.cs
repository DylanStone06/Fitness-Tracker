using System.Net;
using FitnessTracker.Web.Tests.Infrastructure;
using NodaTime;

namespace FitnessTracker.Web.Tests.Routines;

// CAL-9: routines, kinds of week ("Usual week", "Holiday") that repeat over 1 to 4 weeks (A/B weeks). Week A is the
// week it is first used by default (Q3). Today in tests is Monday 19 October 2026. Another user's routines are covered
// by T19 in Security/AccessControlTests.
public sealed class RoutinesTests(TestAppFactory factory) : IClassFixture<TestAppFactory>
{
    // A day in the middle of the week is kept as its week's first day, so the list can say "the week of Mon 26 Oct".
    [Fact]
    public async Task Add_SavesEveryField()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var name = TestBrowser.NewName("Usual week");

        var response = await browser.AddRoutineAsync(name, new() { ["CycleWeeks"] = "2", ["WeekA"] = "2026-10-28", ["IsUsual"] = "true" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var saved = await StoredData.RoutineNamedAsync(factory, name);
        Assert.NotNull(saved);
        Assert.Equal(2, saved.CycleWeeks);
        Assert.Equal(new LocalDate(2026, 10, 26), saved.WeekAStarts);
        Assert.Equal(IsoDayOfWeek.Monday, saved.WeekStart);
        Assert.True(saved.IsUsual);
    }

    // Like a repeat rule, a routine keeps the week start it was added with, so changing the preference later doesn't
    // move which days are in week A.
    [Fact]
    public async Task Add_TakesItsWeeksFromPreferences()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        await browser.SavePreferencesAsync(new() { ["WeekStart"] = "Sunday" });
        var name = TestBrowser.NewName("Usual week");

        await browser.AddRoutineAsync(name, new() { ["CycleWeeks"] = "2", ["WeekA"] = "2026-10-28" });

        var saved = await StoredData.RoutineNamedAsync(factory, name);
        Assert.Equal(new LocalDate(2026, 10, 25), saved!.WeekAStarts);
        Assert.Equal(IsoDayOfWeek.Sunday, saved.WeekStart);
    }

    // Q3: week A is the week it is first used, so the form starts on this week; the first routine is the usual one.
    [Fact]
    public async Task TheAddForm_StartsOnThisWeek_AndIsUsualOnlyWhenThereIsNoUsualRoutine()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");

        var first = await (await browser.GetAsync("/Routines/Create")).Content.ReadAsStringAsync();
        await browser.AddRoutineAsync(TestBrowser.NewName("Usual week"), new() { ["IsUsual"] = "true" });
        var second = await (await browser.GetAsync("/Routines/Create")).Content.ReadAsStringAsync();

        Assert.Contains("value=\"2026-10-19\"", first);
        Assert.Contains("checked", UsualBoxIn(first));
        Assert.DoesNotContain("checked", UsualBoxIn(second));
    }

    [Fact]
    public async Task MakingARoutineUsual_MakesTheOtherOneNot()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var usual = TestBrowser.NewName("Usual week");
        var busy = TestBrowser.NewName("Busy week");
        await browser.AddRoutineAsync(usual, new() { ["IsUsual"] = "true" });

        await browser.AddRoutineAsync(busy, new() { ["IsUsual"] = "true" });

        Assert.False((await StoredData.RoutineNamedAsync(factory, usual))!.IsUsual);
        Assert.True((await StoredData.RoutineNamedAsync(factory, busy))!.IsUsual);
    }

    // Two routines with the same name couldn't be told apart when choosing one for an activity.
    [Fact]
    public async Task Add_RefusesANameAlreadyUsed_WhateverItsCapitals()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var name = TestBrowser.NewName("Usual week");
        await browser.AddRoutineAsync(name);

        var response = await browser.AddRoutineAsync(name.ToUpperInvariant());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("You already have a routine called", await response.Content.ReadAsStringAsync());
        Assert.Null(await StoredData.RoutineNamedAsync(factory, name.ToUpperInvariant()));
    }

    // Another user's routine with the same name is no clash: names only need to differ within one person's routines.
    [Fact]
    public async Task Add_AllowsANameSomeoneElseUses()
    {
        var alex = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var sam = await TestBrowser.OpenSignedInAsync(factory, "sam");
        var name = TestBrowser.NewName("Usual week");
        await alex.AddRoutineAsync(name);

        var response = await sam.AddRoutineAsync(name);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Theory]
    [InlineData("Name", "", "Enter a name for the routine.")]
    [InlineData("CycleWeeks", "5", "Choose how many weeks it takes to repeat, from 1 to 4.")]
    [InlineData("CycleWeeks", "0", "Choose how many weeks it takes to repeat, from 1 to 4.")]
    [InlineData("WeekA", "", "Enter a date in week A.")]
    [InlineData("WeekA", "19/10/2026", "Enter a real date in week A, for example 2 November 2026.")]
    [InlineData("WeekA", "0999-01-04", "Enter a date between the years 1900 and 2999.")]
    public async Task Add_RefusesWhatCantBeSaved(string field, string value, string message)
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var name = TestBrowser.NewName("Usual week");

        var response = await browser.AddRoutineAsync(field == "Name" ? "" : name, new() { [field] = value });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(message, WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        Assert.Null(await StoredData.RoutineNamedAsync(factory, name));
    }

    [Fact]
    public async Task Edit_SavesTheChanges()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var name = TestBrowser.NewName("Usual week");
        await browser.AddRoutineAsync(name);
        var id = (await StoredData.RoutineNamedAsync(factory, name))!.Id;
        var renamed = TestBrowser.NewName("Rota");

        var response = await browser.SubmitFormAsync($"/Routines/Edit/{id}", $"/Routines/Edit/{id}",
            TestBrowser.RoutineForm(renamed, new() { ["CycleWeeks"] = "3", ["WeekA"] = "2026-11-04" }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var saved = await StoredData.RoutineNamedAsync(factory, renamed);
        Assert.Equal(id, saved!.Id);
        Assert.Equal(3, saved.CycleWeeks);
        Assert.Equal(new LocalDate(2026, 11, 2), saved.WeekAStarts);
    }

    // Requirements §9: a form left open in one tab can't silently overwrite a save made in another.
    [Fact]
    public async Task Edit_ChangedInAnotherTab_IsNotOverwritten()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var name = TestBrowser.NewName("Usual week");
        await browser.AddRoutineAsync(name);
        var id = (await StoredData.RoutineNamedAsync(factory, name))!.Id;
        var oldTab = await browser.LoadFormAsync($"/Routines/Edit/{id}");
        var newName = TestBrowser.NewName("Changed in the new tab");
        await browser.SubmitFormAsync($"/Routines/Edit/{id}", $"/Routines/Edit/{id}", TestBrowser.RoutineForm(newName));

        var response = await browser.SubmitAsync(oldTab, $"/Routines/Edit/{id}", TestBrowser.RoutineForm(TestBrowser.NewName("From the old tab")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("changed in another tab", await response.Content.ReadAsStringAsync());
        Assert.Equal(id, (await StoredData.RoutineNamedAsync(factory, newName))!.Id);
    }

    [Fact]
    public async Task Delete_DeletesIt()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var name = TestBrowser.NewName("Usual week");
        await browser.AddRoutineAsync(name);
        var id = (await StoredData.RoutineNamedAsync(factory, name))!.Id;

        var response = await browser.SubmitFormAsync($"/Routines/Delete/{id}", $"/Routines/Delete/{id}", []);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Null(await StoredData.RoutineNamedAsync(factory, name));
    }

    // T18 on the page: a two-week routine with week A starting 12 October is in week B on Monday 19 October.
    [Fact]
    public async Task TheList_SaysWhichWeekThisIs()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var name = TestBrowser.NewName("Usual week");
        await browser.AddRoutineAsync(name, new() { ["CycleWeeks"] = "2", ["WeekA"] = "2026-10-12", ["IsUsual"] = "true" });

        var html = WebUtility.HtmlDecode(await (await browser.GetAsync("/Routines")).Content.ReadAsStringAsync());

        Assert.Contains(name, html);
        Assert.Contains("Repeats every 2 weeks: week A, then week B", html);
        Assert.Contains("This week is week B", html);
        Assert.Contains("Your usual routine", html);
    }

    // The "usual routine" checkbox's tag, as the page renders it.
    private static string UsualBoxIn(string html)
    {
        var box = System.Text.RegularExpressions.Regex.Match(html, """<input[^>]*\sid="IsUsual"[^>]*>""");
        Assert.True(box.Success, "The form has no usual-routine checkbox.");
        return box.Value;
    }
}
