using System.Net;
using FitnessTracker.Web.Tests.Infrastructure;
using NodaTime;

namespace FitnessTracker.Web.Tests.Locations;

// TRV-1: a location's opening hours per weekday (closed days and closing after midnight included), and which
// location is home, the base that travel is measured from (§7.1).
public sealed class LocationOpeningHoursTests(TestAppFactory factory) : IClassFixture<TestAppFactory>
{
    private static readonly IsoDayOfWeek[] Week =
    [
        IsoDayOfWeek.Monday, IsoDayOfWeek.Tuesday, IsoDayOfWeek.Wednesday, IsoDayOfWeek.Thursday,
        IsoDayOfWeek.Friday, IsoDayOfWeek.Saturday, IsoDayOfWeek.Sunday,
    ];

    [Fact]
    public async Task Add_WithOpeningHours_SavesThem_ClosedDaysAndAfterMidnightIncluded()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var name = TestBrowser.NewName("Town gym");

        var response = await AddAsync(browser, name, Hours(weekdays: ("06:00", "22:00"), saturday: ("18:00", "02:00"), sunday: null));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = (await StoredData.LocationNamedAsync(factory, name))!;
        Assert.True(location.HasOpeningHours);
        var times = await StoredData.OpeningTimesOfAsync(factory, location.Id);
        Assert.Equal(6, times.Count);
        Assert.DoesNotContain(times, time => time.Day == IsoDayOfWeek.Sunday);
        var saturday = Assert.Single(times, time => time.Day == IsoDayOfWeek.Saturday);
        Assert.Equal((new LocalTime(18, 0), new LocalTime(2, 0)), (saturday.Opens, saturday.Closes));
    }

    [Fact]
    public async Task Add_WithoutOpeningHours_IsAlwaysOpen()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var name = TestBrowser.NewName("Park");

        await AddAsync(browser, name, new() { ["HasOpeningHours"] = "false" });

        var location = (await StoredData.LocationNamedAsync(factory, name))!;
        Assert.False(location.HasOpeningHours);
        Assert.Empty(await StoredData.OpeningTimesOfAsync(factory, location.Id));
    }

    // An open day needs both times; the problem links to the field to fix.
    [Fact]
    public async Task Add_ShowsAnError_LinkedToTheField_WhenAnOpenDayHasNoTimes()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var name = TestBrowser.NewName("Town gym");
        var form = Hours(weekdays: ("06:00", "22:00"), saturday: null, sunday: null);
        form["OpeningDays[0].Opens"] = "";

        var response = await AddAsync(browser, name, form);

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("<title>Error: ", html);
        Assert.Contains("""href="#OpeningDays_0__Opens""", html);
        Assert.Null(await StoredData.LocationNamedAsync(factory, name));
    }

    [Fact]
    public async Task Add_ShowsAnError_WhenATimeIsNotATime()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var name = TestBrowser.NewName("Town gym");
        var form = Hours(weekdays: ("06:00", "22:00"), saturday: null, sunday: null);
        form["OpeningDays[2].Closes"] = "25:00";

        var response = await AddAsync(browser, name, form);

        Assert.Contains("""href="#OpeningDays_2__Closes""", await response.Content.ReadAsStringAsync());
        Assert.Null(await StoredData.LocationNamedAsync(factory, name));
    }

    // Opening hours with every day closed is almost certainly a mistake (it would mean never open).
    [Fact]
    public async Task Add_ShowsAnError_WhenOpeningHoursAreSetButEveryDayIsClosed()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var name = TestBrowser.NewName("Town gym");

        var response = await AddAsync(browser, name, Hours(weekdays: null, saturday: null, sunday: null));

        Assert.Contains("<title>Error: ", await response.Content.ReadAsStringAsync());
        Assert.Null(await StoredData.LocationNamedAsync(factory, name));
    }

    [Fact]
    public async Task Edit_ReplacesTheOpeningHours_AndThePageShowsTheNewOnes()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var name = TestBrowser.NewName("Town gym");
        await AddAsync(browser, name, Hours(weekdays: ("06:00", "22:00"), saturday: null, sunday: null));
        var id = (await StoredData.LocationNamedAsync(factory, name))!.Id;

        var response = await browser.SubmitFormAsync($"/Locations/Edit/{id}", $"/Locations/Edit/{id}",
            WithName(name, Hours(weekdays: ("07:00", "21:00"), saturday: ("09:00", "17:00"), sunday: null)));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var times = await StoredData.OpeningTimesOfAsync(factory, id);
        Assert.Equal(6, times.Count);
        Assert.All(times.Where(time => time.Day != IsoDayOfWeek.Saturday), time => Assert.Equal(new LocalTime(7, 0), time.Opens));
        var page = await (await browser.GetAsync($"/Locations/Edit/{id}")).Content.ReadAsStringAsync();
        Assert.Contains("value=\"07:00\"", page);
    }

    // Requirements §9: changing only the opening hours still counts as changing the location, so a tab opened
    // before that change can't overwrite it.
    [Fact]
    public async Task Edit_FromAnOutOfDateTab_CantOverwriteNewerOpeningHours()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var name = TestBrowser.NewName("Town gym");
        await AddAsync(browser, name, Hours(weekdays: ("06:00", "22:00"), saturday: null, sunday: null));
        var id = (await StoredData.LocationNamedAsync(factory, name))!.Id;
        var editUrl = $"/Locations/Edit/{id}";
        var oldTab = await browser.LoadFormAsync(editUrl);
        await browser.SubmitFormAsync(editUrl, editUrl, WithName(name, Hours(weekdays: ("07:00", "21:00"), saturday: null, sunday: null)));

        var response = await browser.SubmitAsync(oldTab, editUrl, WithName(name, Hours(weekdays: ("05:00", "23:00"), saturday: null, sunday: null)));

        Assert.Contains("changed in another tab", await response.Content.ReadAsStringAsync());
        Assert.All(await StoredData.OpeningTimesOfAsync(factory, id), time => Assert.Equal(new LocalTime(7, 0), time.Opens));
    }

    // §7.1: travel is measured from home, so only one location can be home.
    [Fact]
    public async Task MarkingALocationAsHome_UnmarksThePreviousHome()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var flat = TestBrowser.NewName("Flat");
        var house = TestBrowser.NewName("House");
        await AddAsync(browser, flat, new() { ["IsHome"] = "true" });

        await AddAsync(browser, house, new() { ["IsHome"] = "true" });

        Assert.False((await StoredData.LocationNamedAsync(factory, flat))!.IsHome);
        Assert.True((await StoredData.LocationNamedAsync(factory, house))!.IsHome);
    }

    // Another person's home is untouched when someone marks their own.
    [Fact]
    public async Task MarkingAHome_LeavesOtherPeoplesHomesAlone()
    {
        var alex = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var alexsFlat = TestBrowser.NewName("Alex flat");
        await AddAsync(alex, alexsFlat, new() { ["IsHome"] = "true" });
        var sam = await TestBrowser.OpenSignedInAsync(factory, "sam");

        await AddAsync(sam, TestBrowser.NewName("Sam house"), new() { ["IsHome"] = "true" });

        Assert.True((await StoredData.LocationNamedAsync(factory, alexsFlat))!.IsHome);
    }

    private static Task<HttpResponseMessage> AddAsync(TestBrowser browser, string name, Dictionary<string, string> fields) =>
        browser.SubmitFormAsync("/Locations/Create", "/Locations/Create", WithName(name, fields));

    private static Dictionary<string, string> WithName(string name, Dictionary<string, string> fields) =>
        new(fields) { ["Name"] = name };

    // The opening-hours fields as the form posts them: one entry per weekday, Monday first. Null means closed.
    private static Dictionary<string, string> Hours((string Opens, string Closes)? weekdays, (string Opens, string Closes)? saturday, (string Opens, string Closes)? sunday)
    {
        var fields = new Dictionary<string, string> { ["HasOpeningHours"] = "true" };
        for (var i = 0; i < Week.Length; i++)
        {
            var hours = Week[i] switch
            {
                IsoDayOfWeek.Saturday => saturday,
                IsoDayOfWeek.Sunday => sunday,
                _ => weekdays,
            };
            fields[$"OpeningDays[{i}].Day"] = Week[i].ToString();
            fields[$"OpeningDays[{i}].Closed"] = hours is null ? "true" : "false";
            fields[$"OpeningDays[{i}].Opens"] = hours?.Opens ?? "";
            fields[$"OpeningDays[{i}].Closes"] = hours?.Closes ?? "";
        }

        return fields;
    }
}
