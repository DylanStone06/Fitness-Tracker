using System.Net;
using System.Text.RegularExpressions;
using FitnessTracker.Web.Data;
using FitnessTracker.Web.Tests.Infrastructure;
using NodaTime;

namespace FitnessTracker.Web.Tests.Preferences;

// ACC-3 (timezone, week start, clock, units), ACC-4 (sleep) and ACC-7 (exercise): one set of preferences per user,
// with the defaults from requirements §3 until they are first saved.
public sealed class PreferencesTests(TestAppFactory factory) : IClassFixture<TestAppFactory>
{
    [Fact]
    public async Task BeforeAnythingIsSaved_ThePageShowsTheDefaults()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");

        var html = await PageAsync(browser);

        Assert.Equal("Europe/London", SelectedValue(html, "TimeZoneId"));
        Assert.Equal("Monday", SelectedValue(html, "WeekStart"));
        Assert.Equal("8", InputValue(html, "TargetSleepHours"));
        Assert.Equal("7", InputValue(html, "MinimumSleepHours"));
        Assert.Equal("23:00", InputValue(html, "UsualBedtime"));
        Assert.Equal("07:00", InputValue(html, "UsualWakeTime"));
        Assert.Equal("30", InputValue(html, "WindDownMinutes"));
        Assert.Equal("30", InputValue(html, "WakeUpRoutineMinutes"));
    }

    [Fact]
    public async Task Save_StoresThePreferences_AndThePageShowsThemNextTime()
    {
        var (browser, email) = await SignedInAsync("alex");

        var response = await SaveAsync(browser, new()
        {
            ["TimeZoneId"] = "America/New_York",
            ["WeekStart"] = "Sunday",
            ["Use24HourClock"] = "false",
            ["DistanceUnit"] = "Miles",
            ["WeightUnit"] = "Pounds",
            ["TargetSleepHours"] = "7.5",
            ["UsualBedtime"] = "22:30",
            ["PreferredExerciseDays"] = "Tuesday",
            ["MaximumDemandingSessionsPerDay"] = "2",
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var saved = await StoredData.PreferencesOfAsync(factory, email);
        Assert.NotNull(saved);
        Assert.Equal("America/New_York", saved.TimeZoneId);
        Assert.Equal(IsoDayOfWeek.Sunday, saved.WeekStart);
        Assert.False(saved.Use24HourClock);
        Assert.Equal(DistanceUnit.Miles, saved.DistanceUnit);
        Assert.Equal(WeightUnit.Pounds, saved.WeightUnit);
        Assert.Equal(450, saved.TargetSleepMinutes);
        Assert.Equal(new LocalTime(22, 30), saved.UsualBedtime);
        Assert.Equal(DaysOfWeek.Tuesday, saved.PreferredExerciseDays);
        Assert.Equal(2, saved.MaximumDemandingSessionsPerDay);

        var html = await PageAsync(browser);
        Assert.Equal("America/New_York", SelectedValue(html, "TimeZoneId"));
        Assert.Equal("7.5", InputValue(html, "TargetSleepHours"));
    }

    // Hours are stored as whole minutes: 8.08 hours is 485 minutes, shown back as 8.08, not 8.0833333...
    [Fact]
    public async Task SleepHours_AreShownBackToTwoDecimalPlaces()
    {
        var (browser, email) = await SignedInAsync("alex");

        await SaveAsync(browser, new() { ["TargetSleepHours"] = "8.08" });

        Assert.Equal(485, (await StoredData.PreferencesOfAsync(factory, email))!.TargetSleepMinutes);
        Assert.Equal("8.08", InputValue(await PageAsync(browser), "TargetSleepHours"));
    }

    [Fact]
    public async Task SavingTwice_KeepsOneSetOfPreferences()
    {
        var (browser, email) = await SignedInAsync("alex");

        await SaveAsync(browser, new() { ["WindDownMinutes"] = "20" });
        await SaveAsync(browser, new() { ["WindDownMinutes"] = "45" });

        Assert.Equal(1, await StoredData.PreferencesCountOfAsync(factory, email));
        Assert.Equal(45, (await StoredData.PreferencesOfAsync(factory, email))!.WindDownMinutes);
    }

    [Theory]
    [InlineData("TimeZoneId", "Mars/Olympus_Mons")]
    [InlineData("TargetSleepHours", "2")]
    [InlineData("TargetSleepHours", "15")]
    [InlineData("UsualBedtime", "25:00")]
    [InlineData("WindDownMinutes", "-5")]
    [InlineData("MinimumHoursBetweenDemandingSessions", "-1")]
    [InlineData("MaximumDemandingSessionsPerDay", "0")]
    // Choices that aren't on the lists, including numbers, which bind to an enum: the model binder refuses them.
    [InlineData("WeekStart", "0")]
    [InlineData("DistanceUnit", "9")]
    [InlineData("WeightUnit", "Stone")]
    [InlineData("PreferredTimeOfDay", "7")]
    [InlineData("Appearance", "Purple")]
    [InlineData("Appearance", "5")]
    public async Task Save_ShowsAnError_AndSavesNothing_ForAValueThatCantBeRight(string field, string value)
    {
        var (browser, email) = await SignedInAsync("alex");

        var response = await SaveAsync(browser, new() { [field] = value });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<title>Error: ", await response.Content.ReadAsStringAsync());
        Assert.Null(await StoredData.PreferencesOfAsync(factory, email));
    }

    // ACC-4: the minimum is the least sleep that is acceptable, so it can't be more than the target.
    [Fact]
    public async Task Save_ShowsAnError_WhenMinimumSleepIsMoreThanTheTarget()
    {
        var (browser, email) = await SignedInAsync("alex");

        var response = await SaveAsync(browser, new() { ["TargetSleepHours"] = "7", ["MinimumSleepHours"] = "8" });

        Assert.Contains("<title>Error: ", await response.Content.ReadAsStringAsync());
        Assert.Null(await StoredData.PreferencesOfAsync(factory, email));
    }

    // GOV.UK error pattern: every problem is listed at once, so fixing one never reveals another. Here a field's own
    // range check (wind-down) and the checks across the form (sleep minimum, timezone, a mistyped time) all fail.
    [Fact]
    public async Task Save_ListsEveryProblemAtOnce()
    {
        var (browser, _) = await SignedInAsync("alex");

        var response = await SaveAsync(browser, new()
        {
            ["WindDownMinutes"] = "-5",
            ["TargetSleepHours"] = "7",
            ["MinimumSleepHours"] = "8",
            ["TimeZoneId"] = "Mars/Olympus_Mons",
            ["UsualWakeTime"] = "7 o'clock",
        });

        var linked = Regex.Matches(await response.Content.ReadAsStringAsync(), """<li><a href="#(\w+)">""")
            .Select(link => link.Groups[1].Value)
            .Order(StringComparer.Ordinal);
        Assert.Equal(["MinimumSleepHours", "TimeZoneId", "UsualWakeTime", "WindDownMinutes"], linked);
    }

    // Requirements §9: a form left open in another tab can't silently overwrite a newer save.
    [Fact]
    public async Task Save_FromAnOutOfDateTab_IsNotSaved_AndSaysWhy()
    {
        var (browser, email) = await SignedInAsync("alex");
        var oldTab = await browser.LoadFormAsync("/Preferences");
        await SaveAsync(browser, new() { ["WindDownMinutes"] = "20" });

        var response = await browser.SubmitAsync(oldTab, "/Preferences", ValidForm(new() { ["WindDownMinutes"] = "45" }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("another tab", await response.Content.ReadAsStringAsync());
        Assert.Equal(20, (await StoredData.PreferencesOfAsync(factory, email))!.WindDownMinutes);
    }

    // T19 for an endpoint without an id: each person only ever sees and changes their own preferences.
    [Fact]
    public async Task AnotherUsersPreferences_AreNeverShownOrChanged()
    {
        var (alex, alexsEmail) = await SignedInAsync("alex");
        await SaveAsync(alex, new() { ["TimeZoneId"] = "Asia/Tokyo" });
        var (sam, samsEmail) = await SignedInAsync("sam");

        var samsPage = await PageAsync(sam);
        await SaveAsync(sam, new() { ["TimeZoneId"] = "America/New_York" });

        Assert.Equal("Europe/London", SelectedValue(samsPage, "TimeZoneId"));
        Assert.Equal("Asia/Tokyo", (await StoredData.PreferencesOfAsync(factory, alexsEmail))!.TimeZoneId);
        Assert.Equal("America/New_York", (await StoredData.PreferencesOfAsync(factory, samsEmail))!.TimeZoneId);
    }

    private async Task<(TestBrowser Browser, string Email)> SignedInAsync(string name)
    {
        var browser = TestBrowser.Open(factory);
        var email = TestBrowser.NewEmail(name);
        Assert.Equal(HttpStatusCode.Redirect, (await browser.RegisterAsync(email)).StatusCode);
        return (browser, email);
    }

    private static Task<HttpResponseMessage> SaveAsync(TestBrowser browser, Dictionary<string, string> changes) =>
        browser.SubmitFormAsync("/Preferences", "/Preferences", ValidForm(changes));

    // Every field, as the page fills them for a new user, with the given changes.
    private static Dictionary<string, string> ValidForm(Dictionary<string, string> changes)
    {
        var form = new Dictionary<string, string>
        {
            ["TimeZoneId"] = "Europe/London",
            ["WeekStart"] = "Monday",
            ["Use24HourClock"] = "true",
            ["DistanceUnit"] = "Kilometres",
            ["WeightUnit"] = "Kilograms",
            ["TargetSleepHours"] = "8",
            ["MinimumSleepHours"] = "7",
            ["UsualBedtime"] = "23:00",
            ["UsualWakeTime"] = "07:00",
            ["WindDownMinutes"] = "30",
            ["WakeUpRoutineMinutes"] = "30",
            ["PreferredTimeOfDay"] = "Any",
            ["MinimumHoursBetweenDemandingSessions"] = "24",
            ["MaximumDemandingSessionsPerDay"] = "1",
        };
        foreach (var (name, value) in changes)
        {
            form[name] = value;
        }

        return form;
    }

    private static async Task<string> PageAsync(TestBrowser browser)
    {
        var response = await browser.GetAsync("/Preferences");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private static string? InputValue(string html, string name)
    {
        var input = Regex.Match(html, $"""<input[^>]*\sname="{name}"[^>]*>""");
        var value = Regex.Match(input.Value, """\svalue="([^"]*)" """.TrimEnd());
        return value.Success ? WebUtility.HtmlDecode(value.Groups[1].Value) : null;
    }

    private static string? SelectedValue(string html, string name)
    {
        var select = Regex.Match(html, $"""<select[^>]*\sname="{name}"[^>]*>(.*?)</select>""", RegexOptions.Singleline);
        var option = Regex.Match(select.Groups[1].Value, """<option[^>]*\sselected="selected"[^>]*\svalue="([^"]*)"|<option[^>]*\svalue="([^"]*)"[^>]*\sselected="selected" """.TrimEnd());
        return option.Success ? WebUtility.HtmlDecode(option.Groups[1].Success ? option.Groups[1].Value : option.Groups[2].Value) : null;
    }
}
