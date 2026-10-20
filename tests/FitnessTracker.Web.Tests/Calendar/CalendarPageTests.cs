using System.Net;
using System.Text.RegularExpressions;
using FitnessTracker.Web.Data;
using FitnessTracker.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FitnessTracker.Web.Tests.Calendar;

// CAL-1: the calendar page. FullCalendar draws it in the browser from /Calendar/Events; the page gives it the user's
// settings, and the one-time code (nonce) its styles need under the Content-Security-Policy (§9).
public sealed class CalendarPageTests(TestAppFactory factory) : IClassFixture<TestAppFactory>
{
    [Fact]
    public async Task ThePage_LoadsFullCalendar_WithTheUsersSettings()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        await browser.SubmitFormAsync("/Preferences", "/Preferences", PreferencesForm(weekStart: "Sunday", use24HourClock: "false"));

        var html = await PageAsync(browser);

        Assert.Contains("/lib/fullcalendar/index.global.min.js", html);
        Assert.Contains("/js/calendar.js", html);
        var calendar = Regex.Match(html, """<div[^>]*\sid="calendar"[^>]*>""").Value;
        Assert.Contains("""data-events-url="/Calendar/Events" """.TrimEnd(), calendar);
        Assert.Contains("""data-time-zone="Europe/London" """.TrimEnd(), calendar);
        Assert.Contains("""data-first-day="0" """.TrimEnd(), calendar);
        Assert.Contains("""data-hour12="true" """.TrimEnd(), calendar);
    }

    // §9: FullCalendar adds its styles with <style> elements. Only this page allows them, by a one-time code that the
    // page hands FullCalendar; never 'unsafe-inline'.
    [Fact]
    public async Task ThePage_AllowsFullCalendarsStyles_ByAOneTimeCode()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");

        var first = await browser.GetAsync("/Calendar");
        var second = await browser.GetAsync("/Calendar");

        var nonce = NonceIn(await first.Content.ReadAsStringAsync());
        var policy = Assert.Single(first.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains($"style-src 'self' 'nonce-{nonce}'", policy);
        Assert.DoesNotContain("unsafe-inline", policy);
        Assert.True(nonce.Length >= 22, "The code should be at least 128 bits.");
        Assert.NotEqual(nonce, NonceIn(await second.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task OtherPages_KeepTheStricterPolicy()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");

        var response = await browser.GetAsync("/Activities");

        Assert.DoesNotContain("style-src", Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
        Assert.DoesNotContain("csp-nonce", await response.Content.ReadAsStringAsync());
    }

    // P1: an activity the app can't read isn't on the calendar, so the page says so (as the home page does).
    [Fact]
    public async Task AnActivityThatCantBeRead_IsNamed()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var title = TestBrowser.NewName("Broken");
        await browser.AddActivityAsync(title, new() { ["StartDate"] = "2027-03-01" });
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Activities.IgnoreQueryFilters()
                .Where(activity => activity.Title == title)
                .ExecuteUpdateAsync(set => set.SetProperty(activity => activity.LengthMinutes, -30));
        }

        Assert.Contains($"{title} couldn&#x27;t be read", await PageAsync(browser));
    }

    [Fact]
    public async Task TheMenu_LinksToTheCalendar()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");

        var html = await (await browser.GetAsync("/")).Content.ReadAsStringAsync();

        Assert.Matches("""<a[^>]*href="/Calendar"[^>]*>Calendar</a>""", html);
    }

    private static string NonceIn(string html)
    {
        var meta = Regex.Match(html, """<meta name="csp-nonce" content="([^"]+)" """.TrimEnd());
        Assert.True(meta.Success, "No csp-nonce meta element.");
        return meta.Groups[1].Value;
    }

    private static async Task<string> PageAsync(TestBrowser browser)
    {
        var response = await browser.GetAsync("/Calendar");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private static Dictionary<string, string> PreferencesForm(string weekStart, string use24HourClock) => new()
    {
        ["TimeZoneId"] = "Europe/London",
        ["WeekStart"] = weekStart,
        ["Use24HourClock"] = use24HourClock,
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
    };
}
