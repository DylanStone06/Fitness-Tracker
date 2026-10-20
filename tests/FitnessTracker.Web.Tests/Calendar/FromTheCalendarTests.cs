using System.Net;
using System.Text.RegularExpressions;
using FitnessTracker.Web.Tests.Infrastructure;

namespace FitnessTracker.Web.Tests.Calendar;

// CAL-1, CAL-2: adding and changing activities from the calendar. Selecting an empty time opens the form with that date
// and time; saving goes back to the calendar on that activity's week, not to the activities list.
public sealed class FromTheCalendarTests(TestAppFactory factory) : IClassFixture<TestAppFactory>
{
    [Fact]
    public async Task TheAddForm_StartsWithTheTimeSelected()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");

        var html = await PageAsync(browser, "/Activities/Create?date=2026-10-21&time=14:30&returnToCalendar=true");

        Assert.Equal("2026-10-21", InputValue(html, "StartDate"));
        Assert.Equal("14:30", InputValue(html, "StartTime"));
        Assert.Equal("true", InputValue(html, "ReturnToCalendar"), ignoreCase: true);
    }

    // Something that isn't a date or time (a link edited by hand) is ignored: the form opens empty, as usual.
    [Fact]
    public async Task TheAddForm_IgnoresADateOrTimeThatCantBeRight()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");

        var html = await PageAsync(browser, "/Activities/Create?date=next%20Tuesday&time=25:99");

        Assert.Null(InputValue(html, "StartDate"));
        Assert.Null(InputValue(html, "StartTime"));
    }

    [Fact]
    public async Task Adding_FromTheCalendar_GoesBackToTheCalendar_OnThatWeek()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");

        var response = await browser.SubmitFormAsync("/Activities/Create?returnToCalendar=true", "/Activities/Create",
            TestBrowser.ActivityForm("Swim", new() { ["StartDate"] = "2026-11-04", ["ReturnToCalendar"] = "true" }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Calendar?date=2026-11-04", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Editing_FromTheCalendar_GoesBackToTheCalendar_OnThatWeek()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var title = TestBrowser.NewName("Swim");
        await browser.AddActivityAsync(title, new() { ["StartDate"] = "2026-11-04" });
        var id = (await StoredData.ActivityTitledAsync(factory, title))!.Id;

        var page = await PageAsync(browser, $"/Activities/Edit/{id}?returnToCalendar=true");
        Assert.Equal("true", InputValue(page, "ReturnToCalendar"), ignoreCase: true);
        var response = await browser.SubmitFormAsync($"/Activities/Edit/{id}?returnToCalendar=true", $"/Activities/Edit/{id}",
            TestBrowser.ActivityForm(title, new() { ["StartDate"] = "2026-11-05", ["ReturnToCalendar"] = "true" }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Calendar?date=2026-11-05", response.Headers.Location?.OriginalString);
    }

    // From the activities list, saving still goes back to the list.
    [Fact]
    public async Task Adding_FromTheList_GoesBackToTheList()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");

        var response = await browser.AddActivityAsync("Swim");

        Assert.Equal("/Activities", response.Headers.Location?.OriginalString);
    }

    // The calendar opens on the week asked for, or today's week when the date can't be read.
    [Theory]
    [InlineData("2026-11-04", "2026-11-04")]
    [InlineData("soon", null)]
    public async Task TheCalendar_OpensOnTheDateAsked(string date, string? expected)
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");

        var calendar = Regex.Match(await PageAsync(browser, $"/Calendar?date={date}"), """<div[^>]*\sid="calendar"[^>]*>""").Value;

        // Empty means today's week.
        var initial = Regex.Match(calendar, """\sdata-initial-date="([^"]*)" """.TrimEnd());
        Assert.True(initial.Success, "No data-initial-date.");
        Assert.Equal(expected ?? "", initial.Groups[1].Value);
    }

    private static async Task<string> PageAsync(TestBrowser browser, string url)
    {
        var response = await browser.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private static string? InputValue(string html, string name)
    {
        var input = Regex.Match(html, $"""<input[^>]*\sname="{name}"[^>]*>""");
        var value = Regex.Match(input.Value, """\svalue="([^"]*)" """.TrimEnd());
        return value.Success && value.Groups[1].Value.Length > 0 ? WebUtility.HtmlDecode(value.Groups[1].Value) : null;
    }
}
