using System.Net;
using System.Text.Json;
using FitnessTracker.Web.Tests.Infrastructure;

namespace FitnessTracker.Web.Tests.Preferences;

// ACC-3: with the 12-hour clock chosen in Preferences, every page shows times that way ("6:00pm"); form boxes still
// take the browser's 24-hour time. "Today" is Monday 19 October 2026 (FixedClock).
public sealed class ClockPreferenceTests(TestAppFactory factory) : IClassFixture<TestAppFactory>
{
    [Fact]
    public async Task TheHomePage_ShowsTwelveHourTimes()
    {
        var browser = await TwelveHourUserAsync();
        await browser.AddActivityAsync("Swim", new() { ["StartDate"] = "2026-10-19", ["StartTime"] = "18:00" });

        Assert.Contains("<span class=\"ft-time\">6:00pm</span>", await PageAsync(browser, "/"));
    }

    [Fact]
    public async Task TheActivitiesList_ShowsTwelveHourTimes()
    {
        var browser = await TwelveHourUserAsync();
        await browser.AddActivityAsync("Swim", new() { ["StartDate"] = "2026-10-19", ["StartTime"] = "18:00" });

        Assert.Contains("Mon 19 Oct 2026 at 6:00pm for 1 h", await PageAsync(browser, "/Activities"));
    }

    [Fact]
    public async Task TheCalendarDetails_AreTwelveHour()
    {
        var browser = await TwelveHourUserAsync();
        await browser.AddActivityAsync("Swim", new() { ["StartDate"] = "2026-10-19", ["StartTime"] = "18:00", ["PreparationMinutes"] = "15" });

        using var json = JsonDocument.Parse(await PageAsync(browser, "/Calendar/Events?start=2026-10-19&end=2026-10-20"));
        var details = Assert.Single(json.RootElement.EnumerateArray()).GetProperty("extendedProps");

        Assert.Equal("6:00pm", details.GetProperty("starts").GetString());
        Assert.Equal("With getting ready and changing: 5:45pm to 7:00pm", details.GetProperty("trip").GetString());
    }

    [Fact]
    public async Task TheOccurrencePage_ShowsTwelveHourTimes()
    {
        var browser = await TwelveHourUserAsync();
        var title = TestBrowser.NewName("Gym");
        await browser.AddActivityAsync(title, new() { ["StartDate"] = "2026-11-02", ["StartTime"] = "07:00", ["Repeats"] = "Daily" });
        var id = (await StoredData.ActivityTitledAsync(factory, title))!.Id;

        var html = await PageAsync(browser, $"/Activities/Occurrence/{id}?date=2026-11-03");

        Assert.Contains("Tue 3 Nov 2026 at 7:00am for 1 h", html);
        Assert.Contains("Every day at 7:00am for 1 h", html);
        Assert.Contains("value=\"07:00\"", html);
    }

    // The check's own explanations follow it too: a clash lists the other activity's times that way.
    [Fact]
    public async Task AClash_IsDescribedWithTwelveHourTimes()
    {
        var browser = await TwelveHourUserAsync();
        await browser.AddActivityAsync("Team meeting", new() { ["StartDate"] = "2026-10-20", ["StartTime"] = "15:00" });

        var response = await browser.SubmitFormAsync("/Activities/Create", "/Activities/Create",
            TestBrowser.ActivityForm("Call", new() { ["StartDate"] = "2026-10-20", ["StartTime"] = "15:30" }));

        Assert.Contains("Team meeting is 3:00pm to 4:00pm", await response.Content.ReadAsStringAsync());
    }

    private async Task<TestBrowser> TwelveHourUserAsync()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var saved = await browser.SubmitFormAsync("/Preferences", "/Preferences", new()
        {
            ["TimeZoneId"] = "Europe/London",
            ["WeekStart"] = "Monday",
            ["Use24HourClock"] = "false",
            ["DistanceUnit"] = "Kilometres",
            ["WeightUnit"] = "Kilograms",
            ["Appearance"] = "Dark",
            ["TargetSleepHours"] = "8",
            ["MinimumSleepHours"] = "7",
            ["UsualBedtime"] = "23:00",
            ["UsualWakeTime"] = "07:00",
            ["WindDownMinutes"] = "30",
            ["WakeUpRoutineMinutes"] = "30",
            ["PreferredTimeOfDay"] = "Any",
            ["MinimumHoursBetweenDemandingSessions"] = "24",
            ["MaximumDemandingSessionsPerDay"] = "1",
        });
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        return browser;
    }

    private static async Task<string> PageAsync(TestBrowser browser, string url)
    {
        var response = await browser.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }
}
