using System.Net;
using System.Text.Json;
using FitnessTracker.Web.Tests.Infrastructure;

namespace FitnessTracker.Web.Tests.Calendar;

// CAL-1: the calendar's data. The signed-in user's occurrences on the days asked for, as clock times where they are,
// with no offset (FullCalendar shows them as given). "Today" is Monday 19 October 2026 (FixedClock); London is on BST
// until the 25th.
public sealed class CalendarTests(TestAppFactory factory) : IClassFixture<TestAppFactory>
{
    [Fact]
    public async Task Events_AreTheOccurrencesOnTheDaysAsked_InClockTime()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        await browser.AddActivityAsync("Team meeting", new()
        {
            ["Category"] = "Work",
            ["StartDate"] = "2026-10-19",
            ["StartTime"] = "15:00",
            ["Repeats"] = "Weekly",
            ["RepeatDays[0]"] = "true",
        });

        // The window's end is exclusive, so the next Monday's meeting (the 26th) isn't in it.
        var meeting = Assert.Single(await EventsAsync(browser, "2026-10-19T00:00:00", "2026-10-26T00:00:00"));

        Assert.Equal("Team meeting", meeting.GetProperty("title").GetString());
        Assert.Equal("2026-10-19T15:00:00", meeting.GetProperty("start").GetString());
        Assert.Equal("2026-10-19T16:00:00", meeting.GetProperty("end").GetString());
        Assert.Contains("cat-work", Strings(meeting.GetProperty("classNames")));
        Assert.Equal("Work", Details(meeting).GetProperty("category").GetString());
    }

    // T1 and CAL-5: the footprint's parts, to draw as lighter bands above and below the activity. Gym 18:00-18:50 at Town gym:
    // 25 min there, 10 min changing, 45 min back.
    [Fact]
    public async Task Events_CarryTheFootprint_AndWhenToLeave()
    {
        var (browser, gym) = await SignedInWithHomeAndGymAsync();
        await browser.AddActivityAsync("Evening gym", new()
        {
            ["LocationId"] = gym.ToString(),
            ["StartDate"] = "2026-10-19",
            ["StartTime"] = "18:00",
            ["DurationHours"] = "0",
            ["DurationMinutes"] = "50",
            ["ChangingMinutes"] = "10",
        });

        var details = Details(Assert.Single(await EventsAsync(browser, "2026-10-19", "2026-10-20")));

        Assert.Equal("Leave home 17:35, back 19:45", details.GetProperty("trip").GetString());
        Assert.Equal(
            [
                ("TravelOut", "2026-10-19T17:35:00", "2026-10-19T18:00:00"),
                ("Changing", "2026-10-19T18:50:00", "2026-10-19T19:00:00"),
                ("TravelBack", "2026-10-19T19:00:00", "2026-10-19T19:45:00"),
            ],
            details.GetProperty("footprint").EnumerateArray().Select(segment =>
                (segment.GetProperty("part").GetString(), segment.GetProperty("start").GetString(), segment.GetProperty("end").GetString())));
    }

    // CAL-6: a clash kept with "Save anyway" is flagged on both, in words as well as a class.
    [Fact]
    public async Task Events_FlagClashes()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        await browser.AddActivityAsync("Team meeting", new() { ["StartDate"] = "2026-10-19", ["StartTime"] = "15:00" });
        var clashing = TestBrowser.ActivityForm("Call", new() { ["StartDate"] = "2026-10-19", ["StartTime"] = "15:30" });
        var shown = TestBrowser.FormIn(await (await browser.SubmitFormAsync("/Activities/Create", "/Activities/Create", clashing)).Content.ReadAsStringAsync());
        await browser.SubmitAsync(shown, "/Activities/Create", new(clashing) { ["ConfirmClashes"] = "true" });

        var call = (await EventsAsync(browser, "2026-10-19", "2026-10-20")).Single(item => item.GetProperty("title").GetString() == "Call");

        Assert.Contains("ft-clash", Strings(call.GetProperty("classNames")));
        Assert.Equal(["Team meeting"], Strings(Details(call).GetProperty("clashesWith")));
    }

    // A night shift from the evening before runs into the day asked for, so it is on screen.
    [Fact]
    public async Task Events_IncludeWhatRunsIntoTheDays_FromTheDayBefore()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        await browser.AddActivityAsync("Night shift", new() { ["StartDate"] = "2026-10-18", ["StartTime"] = "22:00", ["DurationHours"] = "8" });

        var shift = Assert.Single(await EventsAsync(browser, "2026-10-19", "2026-10-20"));

        Assert.Equal("2026-10-18T22:00:00", shift.GetProperty("start").GetString());
        Assert.Equal("2026-10-19T06:00:00", shift.GetProperty("end").GetString());
        // What the calendar writes: the day it ends, not the midnight a day's part of it stops at (break-ui, Phase 5).
        Assert.Equal("22:00", Details(shift).GetProperty("starts").GetString());
        Assert.Equal("06:00 the next day", Details(shift).GetProperty("ends").GetString());
        Assert.Equal("06:00", Details(shift).GetProperty("endsAt").GetString());
    }

    // T4, §8: on the night the clocks go back, 01:30 is the first one (BST), and the event says which.
    [Fact]
    public async Task Events_OnTheNightTheClocksGoBack_SayWhich0130()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        await browser.AddActivityAsync("Late session", new() { ["StartDate"] = "2026-10-25", ["StartTime"] = "01:30", ["DurationHours"] = "0", ["DurationMinutes"] = "20" });

        var session = Assert.Single(await EventsAsync(browser, "2026-10-25", "2026-10-26"));

        Assert.Equal("2026-10-25T01:30:00", session.GetProperty("start").GetString());
        Assert.Equal("01:30 BST", Details(session).GetProperty("starts").GetString());
    }

    // CAL-4: an occurrence of a repeating activity opens its own page ("just this one"), so the feed says it repeats;
    // a moved one says so.
    [Fact]
    public async Task Events_SayWhetherTheyRepeat_AndWhetherTheyWereMoved()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        await browser.AddActivityAsync("Dentist", new() { ["StartDate"] = "2026-10-20", ["StartTime"] = "09:00" });
        var gymTitle = TestBrowser.NewName("Gym");
        await browser.AddActivityAsync(gymTitle, new() { ["StartDate"] = "2026-10-19", ["StartTime"] = "07:00", ["Repeats"] = "Daily" });
        var gym = (await StoredData.ActivityTitledAsync(factory, gymTitle))!.Id;
        await browser.SubmitFormAsync($"/Activities/Occurrence/{gym}?date=2026-10-20", $"/Activities/Occurrence/{gym}", new()
        {
            ["Change"] = "Move",
            ["NewDate"] = "2026-10-20",
            ["NewStartTime"] = "18:00",
            ["DurationHours"] = "1",
            ["DurationMinutes"] = "0",
        });

        var events = await EventsAsync(browser, "2026-10-20", "2026-10-21");

        var dentist = Details(events.Single(item => item.GetProperty("title").GetString() == "Dentist"));
        var moved = Details(events.Single(item => item.GetProperty("title").GetString() == gymTitle));
        Assert.False(dentist.GetProperty("repeats").GetBoolean());
        Assert.False(dentist.GetProperty("moved").GetBoolean());
        Assert.True(moved.GetProperty("repeats").GetBoolean());
        Assert.True(moved.GetProperty("moved").GetBoolean());
        Assert.Equal("2026-10-20", moved.GetProperty("occurrenceDate").GetString());
    }

    // CAL-1, CAL-4 (scheduling review M-2): an occurrence moved to before its activity's first date shows where it now
    // is. Mondays from 9 November, the first moved to Friday 30 October.
    [Fact]
    public async Task Events_ShowAnOccurrenceMovedToBeforeTheFirstDate()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var title = TestBrowser.NewName("Gym");
        await browser.AddActivityAsync(title, new() { ["StartDate"] = "2026-11-09", ["StartTime"] = "07:00", ["Repeats"] = "Weekly", ["RepeatDays[0]"] = "true" });
        var id = (await StoredData.ActivityTitledAsync(factory, title))!.Id;
        var moved = await browser.SubmitFormAsync($"/Activities/Occurrence/{id}?date=2026-11-09", $"/Activities/Occurrence/{id}", new()
        {
            ["Date"] = "2026-11-09",
            ["Change"] = "Move",
            ["NewDate"] = "2026-10-30",
            ["NewStartTime"] = "07:00",
            ["DurationHours"] = "1",
            ["DurationMinutes"] = "0",
        });
        Assert.Equal(HttpStatusCode.Redirect, moved.StatusCode);

        var shown = Assert.Single(await EventsAsync(browser, "2026-10-26T00:00:00", "2026-11-02T00:00:00"));

        Assert.Equal("2026-10-30T07:00:00", shown.GetProperty("start").GetString());
    }

    // §8, CAL-8 (scheduling review M-3): 01:45 BST for 30 minutes on 25 October 2026 ends at 01:15 GMT. The event still
    // ends after it starts (drawn as its elapsed length), and carries the real length for a drag to keep.
    [Fact]
    public async Task Events_OnTheNightTheClocksGoBack_NeverEndBeforeTheyStart()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        await browser.AddActivityAsync("Late swim", new() { ["StartDate"] = "2026-10-25", ["StartTime"] = "01:45", ["DurationHours"] = "0", ["DurationMinutes"] = "30" });

        var swim = Assert.Single(await EventsAsync(browser, "2026-10-25", "2026-10-26"));

        Assert.Equal("2026-10-25T01:45:00", swim.GetProperty("start").GetString());
        Assert.Equal("2026-10-25T02:15:00", swim.GetProperty("end").GetString());
        Assert.Equal(30, Details(swim).GetProperty("minutes").GetInt32());
        Assert.Equal(("01:45 BST", "01:15 GMT"), (Details(swim).GetProperty("starts").GetString(), Details(swim).GetProperty("ends").GetString()));
    }

    // On the night the clocks go forward, 00:30 GMT for 2 hours ends at 03:30 BST: shown so, and still 120 minutes long.
    [Fact]
    public async Task Events_OnTheNightTheClocksGoForward_KeepTheirRealLength()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        await browser.AddActivityAsync("Late film", new() { ["StartDate"] = "2027-03-28", ["StartTime"] = "00:30", ["DurationHours"] = "2", ["DurationMinutes"] = "0" });

        var film = Assert.Single(await EventsAsync(browser, "2027-03-28", "2027-03-29"));

        Assert.Equal("2027-03-28T03:30:00", film.GetProperty("end").GetString());
        Assert.Equal(120, Details(film).GetProperty("minutes").GetInt32());
    }

    // T19: only the signed-in user's own occurrences.
    [Fact]
    public async Task AnotherUsersActivities_AreNeverSent()
    {
        var alex = await TestBrowser.OpenSignedInAsync(factory, "alex");
        await alex.AddActivityAsync("Alex's run", new() { ["StartDate"] = "2026-10-19" });
        var sam = await TestBrowser.OpenSignedInAsync(factory, "sam");

        Assert.Empty(await EventsAsync(sam, "2026-10-19", "2026-10-20"));
    }

    [Theory]
    [InlineData(null, "2026-10-26")]
    [InlineData("2026-10-19", null)]
    [InlineData("next week", "2026-10-26")]
    [InlineData("2026-10-26", "2026-10-19")]
    [InlineData("2026-10-19", "2026-10-19")]
    [InlineData("2026-10-01", "2026-12-03")]
    // Review L-1: inside the hour the clocks skip, 01:30 becomes 02:30, after 02:00: no window at all.
    [InlineData("2027-03-28T01:30:00", "2027-03-28T02:00:00")]
    public async Task Events_RefuseAWindowThatCantBeRight(string? start, string? end)
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var query = string.Join("&", new[] { ("start", start), ("end", end) }
            .Where(pair => pair.Item2 is not null)
            .Select(pair => $"{pair.Item1}={Uri.EscapeDataString(pair.Item2!)}"));

        var response = await browser.GetAsync($"/Calendar/Events?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<(TestBrowser Browser, Guid Gym)> SignedInWithHomeAndGymAsync()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var homeName = TestBrowser.NewName("Home");
        var gymName = TestBrowser.NewName("Town gym");
        await browser.AddLocationAsync(homeName, new() { ["IsHome"] = "true" });
        await browser.AddLocationAsync(gymName);
        var home = (await StoredData.LocationNamedAsync(factory, homeName))!.Id;
        var gym = (await StoredData.LocationNamedAsync(factory, gymName))!.Id;
        await browser.AddTravelLegAsync(home, gym, minutes: 25);
        await browser.AddTravelLegAsync(gym, home, minutes: 45);
        return (browser, gym);
    }

    private static async Task<List<JsonElement>> EventsAsync(TestBrowser browser, string start, string end)
    {
        var response = await browser.GetAsync($"/Calendar/Events?start={Uri.EscapeDataString(start)}&end={Uri.EscapeDataString(end)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.EnumerateArray().Select(item => item.Clone()).ToList();
    }

    private static JsonElement Details(JsonElement calendarEvent) => calendarEvent.GetProperty("extendedProps");

    private static List<string?> Strings(JsonElement array) => array.EnumerateArray().Select(item => item.GetString()).ToList();
}
