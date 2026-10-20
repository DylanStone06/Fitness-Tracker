using System.Net;
using System.Text.RegularExpressions;
using FitnessTracker.Web.Data;
using FitnessTracker.Web.Tests.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace FitnessTracker.Web.Tests.Preferences;

// ACC-3, Appearance: pages are dark unless the user chooses light or "match my device" in Preferences (the owner's
// choice, 20 October 2026). The page arrives in its theme, so it never flashes the other one first.
public sealed class AppearanceTests(TestAppFactory factory) : IClassFixture<TestAppFactory>
{
    [Fact]
    public async Task ANewUser_GetsDarkPages()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");

        Assert.Equal("dark", ThemeOf(await PageAsync(browser, "/")));
    }

    [Fact]
    public async Task SignedOutPages_AreDark()
    {
        var browser = TestBrowser.Open(factory);

        Assert.Equal("dark", ThemeOf(await PageAsync(browser, "/Account/Login")));
    }

    [Fact]
    public async Task ChoosingLight_GivesLightPages()
    {
        var browser = TestBrowser.Open(factory);
        var email = TestBrowser.NewEmail("alex");
        await browser.RegisterAsync(email);

        await SaveAppearanceAsync(browser, "Light");

        Assert.Equal(Appearance.Light, (await StoredData.PreferencesOfAsync(factory, email))!.Appearance);
        Assert.Equal("light", ThemeOf(await PageAsync(browser, "/")));
        Assert.Equal("light", ThemeOf(await PageAsync(browser, "/Activities")));
    }

    // "Match my device" arrives light (as it stays without JavaScript), and loads js/theme.js, which switches it to the
    // device's setting before the page is drawn. Dark and light pages don't load it.
    [Fact]
    public async Task MatchMyDevice_LeavesItToTheDevice()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        Assert.DoesNotContain("/js/theme.js", await PageAsync(browser, "/"));

        await SaveAppearanceAsync(browser, "Device");

        var html = await PageAsync(browser, "/");
        Assert.Equal("light", ThemeOf(html));
        Assert.Matches("""<html[^>]*\sdata-theme="device"[^>]*>""", html);
        Assert.Contains("/js/theme.js", html);
    }

    [Fact]
    public async Task ThePreferencesPage_ShowsTheChoice()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");

        Assert.Matches("""<input[^>]*value="Dark"[^>]*checked="checked"|<input[^>]*checked="checked"[^>]*value="Dark" """.TrimEnd(), await PageAsync(browser, "/Preferences"));
        await SaveAppearanceAsync(browser, "Light");
        Assert.Matches("""<input[^>]*value="Light"[^>]*checked="checked"|<input[^>]*checked="checked"[^>]*value="Light" """.TrimEnd(), await PageAsync(browser, "/Preferences"));
    }

    // T19: one person's choice never changes another's pages.
    [Fact]
    public async Task AnotherUsersChoice_DoesNotChangeYourPages()
    {
        var alex = await TestBrowser.OpenSignedInAsync(factory, "alex");
        await SaveAppearanceAsync(alex, "Light");
        var sam = await TestBrowser.OpenSignedInAsync(factory, "sam");

        Assert.Equal("dark", ThemeOf(await PageAsync(sam, "/")));
    }

    // The error page doesn't read the preference: it may be showing a database fault. So it is dark even for someone
    // who chose light.
    [Fact]
    public async Task TheErrorPage_DoesNotReadThePreference()
    {
        var failing = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddTransient<IStartupFilter, FailingPage>()));
        var browser = TestBrowser.Open(failing);
        await browser.RegisterAsync(TestBrowser.NewEmail("alex"));
        await SaveAppearanceAsync(browser, "Light");

        var response = await browser.GetAsync(FailingPage.Path);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Something went wrong", html);
        Assert.Equal("dark", ThemeOf(html));
    }

    /// <summary>A page that always fails, added after the app's own pipeline, so inside its error handling.</summary>
    private sealed class FailingPage : IStartupFilter
    {
        public const string Path = "/test-only/fail";

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Use((context, rest) => context.Request.Path == Path
                ? throw new InvalidOperationException("A page failing, for the test.")
                : rest(context));
        };
    }

    private static Task<HttpResponseMessage> SaveAppearanceAsync(TestBrowser browser, string appearance) =>
        browser.SubmitFormAsync("/Preferences", "/Preferences", new()
        {
            ["TimeZoneId"] = "Europe/London",
            ["WeekStart"] = "Monday",
            ["Use24HourClock"] = "true",
            ["DistanceUnit"] = "Kilometres",
            ["WeightUnit"] = "Kilograms",
            ["Appearance"] = appearance,
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

    private static async Task<string> PageAsync(TestBrowser browser, string url)
    {
        var response = await browser.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>The theme the page arrives in (Bootstrap's data-bs-theme on the html element), or null if none.</summary>
    private static string? ThemeOf(string html)
    {
        var element = Regex.Match(html, "<html[^>]*>");
        Assert.True(element.Success, "No html element.");
        var theme = Regex.Match(element.Value, """\sdata-bs-theme="([^"]*)" """.TrimEnd());
        return theme.Success ? theme.Groups[1].Value : null;
    }
}
