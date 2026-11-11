using System.Net;
using System.Text.RegularExpressions;
using FitnessTracker.Web.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace FitnessTracker.Web.Tests.Security;

// T19 and ACC-2: a signed-in user who asks for someone else's data gets "not found", exactly as if it didn't
// exist ("forbidden" would confirm that it does), and the data is left alone. This is the HTTP side; the data
// layer's side is Data/OwnershipTests.
public sealed partial class AccessControlTests(TestAppFactory factory) : IClassFixture<TestAppFactory>
{
    /// <summary>Every action that takes a record's id. A new one must be added here (the last test checks).</summary>
    public static TheoryData<string, string> ActionsThatTakeAnId => new()
    {
        { "GET", "/Locations/Edit/{id}" },
        { "POST", "/Locations/Edit/{id}" },
        { "GET", "/Locations/Delete/{id}" },
        { "POST", "/Locations/Delete/{id}" },
        { "GET", "/TravelLegs/Edit/{id}" },
        { "POST", "/TravelLegs/Edit/{id}" },
        { "GET", "/TravelLegs/Delete/{id}" },
        { "POST", "/TravelLegs/Delete/{id}" },
        { "GET", "/Activities/Edit/{id}" },
        { "POST", "/Activities/Edit/{id}" },
        { "GET", "/Activities/Delete/{id}" },
        { "POST", "/Activities/Delete/{id}" },
        { "GET", "/Activities/Occurrence/{id}" },
        { "POST", "/Activities/Occurrence/{id}" },
        { "GET", "/Activities/Reschedule/{id}" },
        { "GET", "/Goals/Edit/{id}" },
        { "POST", "/Goals/Edit/{id}" },
        { "GET", "/Goals/Delete/{id}" },
        { "POST", "/Goals/Delete/{id}" },
        { "GET", "/Routines/Edit/{id}" },
        { "POST", "/Routines/Edit/{id}" },
        { "GET", "/Routines/Delete/{id}" },
        { "POST", "/Routines/Delete/{id}" },
        { "GET", "/Routines/Duplicate/{id}" },
        { "POST", "/Routines/Duplicate/{id}" },
        { "GET", "/RoutineDates/Edit/{id}" },
        { "POST", "/RoutineDates/Edit/{id}" },
        { "GET", "/RoutineDates/Delete/{id}" },
        { "POST", "/RoutineDates/Delete/{id}" },
        { "GET", "/Sessions/Log/{id}" },
        { "POST", "/Sessions/Log/{id}" },
    };

    /// <summary>The pages (GET actions) of <see cref="ActionsThatTakeAnId"/>.</summary>
    public static TheoryData<string> PagesThatTakeAnId
    {
        get
        {
            var pages = new TheoryData<string>();
            foreach (var row in ActionsThatTakeAnId.Where(row => (string)row[0] == "GET"))
            {
                pages.Add((string)row[1]);
            }

            return pages;
        }
    }

    // Each action is tried with Alex's record of the kind it handles (a travel leg for /TravelLegs/...): a location's
    // id sent to a travel-leg page would be "not found" anyway and prove nothing.
    [Theory]
    [MemberData(nameof(ActionsThatTakeAnId))]
    public async Task AnotherUsersRecord_IsNotFound_AndLeftAlone(string method, string path)
    {
        var (_, alexsRecord) = await AlexAddsARecordForAsync(path);
        var versionBefore = await StoredData.VersionOfAsync(factory, alexsRecord);
        var sam = await TestBrowser.OpenSignedInAsync(factory, "sam");

        var response = await SendAsync(sam, method, path.Replace("{id}", alexsRecord.ToString()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotNull(versionBefore);
        Assert.Equal(versionBefore, await StoredData.VersionOfAsync(factory, alexsRecord));
    }

    [Theory]
    [MemberData(nameof(ActionsThatTakeAnId))]
    public async Task AnotherUsersRecord_LooksTheSameAsOneThatDoesNotExist(string method, string path)
    {
        var (_, alexsRecord) = await AlexAddsARecordForAsync(path);
        var sam = await TestBrowser.OpenSignedInAsync(factory, "sam");

        var forAlexsRecord = await SendAsync(sam, method, path.Replace("{id}", alexsRecord.ToString()));
        var forNoSuchRecord = await SendAsync(sam, method, path.Replace("{id}", Guid.NewGuid().ToString()));

        Assert.Equal(forNoSuchRecord.StatusCode, forAlexsRecord.StatusCode);
        Assert.Equal(await TitleOfAsync(forNoSuchRecord), await TitleOfAsync(forAlexsRecord));
    }

    // The same page asked for by the record's owner isn't "not found", so Sam's "not found" above comes from the check
    // of whose record it is, not from something else the request lacks (code review of Phase 7: a session's page, and
    // the reschedule page, need a date too, without which even the owner's was "not found").
    [Theory]
    [MemberData(nameof(PagesThatTakeAnId))]
    public async Task TheOwnersOwnRecord_IsFound(string path)
    {
        var (alex, alexsRecord) = await AlexAddsARecordForAsync(path);

        var response = await SendAsync(alex, "GET", path.Replace("{id}", alexsRecord.ToString()));

        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task TravelLegsList_ShowsOnlyTheSignedInUsersOwn()
    {
        _ = await AlexAddsARecordForAsync("/TravelLegs/Edit/{id}");
        var sam = await TestBrowser.OpenSignedInAsync(factory, "sam");

        var html = await (await sam.GetAsync("/TravelLegs")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("Alex", html);
    }

    [Fact]
    public async Task ActivitiesList_ShowsOnlyTheSignedInUsersOwn()
    {
        var alex = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var alexsTitle = TestBrowser.NewName("Alex gym");
        await alex.AddActivityAsync(alexsTitle);
        var sam = await TestBrowser.OpenSignedInAsync(factory, "sam");
        var samsTitle = TestBrowser.NewName("Sam run");
        await sam.AddActivityAsync(samsTitle);

        var html = await (await sam.GetAsync("/Activities")).Content.ReadAsStringAsync();

        Assert.Contains(samsTitle, html);
        Assert.DoesNotContain(alexsTitle, html);
    }

    [Fact]
    public async Task GoalsList_ShowsOnlyTheSignedInUsersOwn()
    {
        var alex = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var alexsTitle = TestBrowser.NewName("Alex gym");
        await alex.AddGoalAsync(alexsTitle);
        var sam = await TestBrowser.OpenSignedInAsync(factory, "sam");
        var samsTitle = TestBrowser.NewName("Sam run");
        await sam.AddGoalAsync(samsTitle);

        var html = await (await sam.GetAsync("/Goals")).Content.ReadAsStringAsync();

        Assert.Contains(samsTitle, html);
        Assert.DoesNotContain(alexsTitle, html);
    }

    [Fact]
    public async Task RoutinesList_ShowsOnlyTheSignedInUsersOwn()
    {
        var alex = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var alexsName = TestBrowser.NewName("Alex rota");
        await alex.AddRoutineAsync(alexsName);
        var sam = await TestBrowser.OpenSignedInAsync(factory, "sam");
        var samsName = TestBrowser.NewName("Sam week");
        await sam.AddRoutineAsync(samsName);

        var html = await (await sam.GetAsync("/Routines")).Content.ReadAsStringAsync();

        Assert.Contains(samsName, html);
        Assert.DoesNotContain(alexsName, html);
    }

    [Fact]
    public async Task LocationsList_ShowsOnlyTheSignedInUsersOwn()
    {
        var (_, alexsName) = await AlexAddsALocationAsync();
        var sam = await TestBrowser.OpenSignedInAsync(factory, "sam");
        var samsName = TestBrowser.NewName("Sam office");
        await sam.AddLocationAsync(samsName);

        var html = await (await sam.GetAsync("/Locations")).Content.ReadAsStringAsync();

        Assert.Contains(samsName, html);
        Assert.DoesNotContain(alexsName, html);
    }

    // CLAUDE.md rule 5: every new data endpoint gets an access-control test. This fails when an action that
    // takes a record's id is added without being listed in ActionsThatTakeAnId above. It only recognises a
    // parameter named exactly "id": keep that name for record ids. (Endpoints that return a user's data without
    // an id, such as the calendar's date-range feed, need their own T19 tests.)
    [Fact]
    public void EveryActionThatTakesAnId_IsListedForTheTestsAbove()
    {
        var actions = factory.Services.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>()
            .Where(action => action.Parameters.Any(parameter => parameter.Name == "id"))
            .SelectMany(action => HttpMethodsOf(action)
                .Select(method => (method, $"/{action.ControllerName}/{action.ActionName}/{{id}}")))
            .Order()
            .ToList();

        var listed = ActionsThatTakeAnId
            .Select(row => ((string)row[0], (string)row[1]))
            .Order()
            .ToList();

        Assert.Equal(listed, actions);
    }

    private async Task<(Guid Id, string Name)> AlexAddsALocationAsync(TestBrowser? alex = null)
    {
        alex ??= await TestBrowser.OpenSignedInAsync(factory, "alex");
        var name = TestBrowser.NewName("Alex gym");
        var added = await alex.AddLocationAsync(name);
        Assert.Equal(HttpStatusCode.Redirect, added.StatusCode);
        return ((await StoredData.LocationNamedAsync(factory, name))!.Id, name);
    }

    // A new user, Alex, and Alex's record of the kind the action handles, found from the controller in its path.
    private async Task<(TestBrowser Alex, Guid Id)> AlexAddsARecordForAsync(string path)
    {
        var alex = await TestBrowser.OpenSignedInAsync(factory, "alex");
        return (alex, await AddsARecordForAsync(alex, path));
    }

    private async Task<Guid> AddsARecordForAsync(TestBrowser alex, string path)
    {
        if (path.StartsWith("/Locations/", StringComparison.Ordinal))
        {
            return (await AlexAddsALocationAsync(alex)).Id;
        }

        if (path.StartsWith("/TravelLegs/", StringComparison.Ordinal))
        {
            var homeName = TestBrowser.NewName("Alex home");
            var gymName = TestBrowser.NewName("Alex gym");
            await alex.AddLocationAsync(homeName);
            await alex.AddLocationAsync(gymName);
            var home = (await StoredData.LocationNamedAsync(factory, homeName))!.Id;
            var gym = (await StoredData.LocationNamedAsync(factory, gymName))!.Id;
            Assert.Equal(HttpStatusCode.Redirect, (await alex.AddTravelLegAsync(home, gym, minutes: 25)).StatusCode);
            return (await StoredData.TravelLegAsync(factory, home, gym))!.Id;
        }

        // A session is an occurrence of an activity, known by the activity's id (a gym session, so it has one: Q20).
        if (path.StartsWith("/Activities/", StringComparison.Ordinal) || path.StartsWith("/Sessions/", StringComparison.Ordinal))
        {
            var title = TestBrowser.NewName("Alex gym");
            Assert.Equal(HttpStatusCode.Redirect, (await alex.AddActivityAsync(title)).StatusCode);
            return (await StoredData.ActivityTitledAsync(factory, title))!.Id;
        }

        if (path.StartsWith("/Goals/", StringComparison.Ordinal))
        {
            var title = TestBrowser.NewName("Alex gym");
            Assert.Equal(HttpStatusCode.Redirect, (await alex.AddGoalAsync(title)).StatusCode);
            return (await StoredData.GoalTitledAsync(factory, title))!.Id;
        }

        if (path.StartsWith("/Routines/", StringComparison.Ordinal))
        {
            var name = TestBrowser.NewName("Alex rota");
            Assert.Equal(HttpStatusCode.Redirect, (await alex.AddRoutineAsync(name)).StatusCode);
            return (await StoredData.RoutineNamedAsync(factory, name))!.Id;
        }

        if (path.StartsWith("/RoutineDates/", StringComparison.Ordinal))
        {
            var name = TestBrowser.NewName("Alex holiday");
            Assert.Equal(HttpStatusCode.Redirect, (await alex.AddRoutineAsync(name)).StatusCode);
            var routine = (await StoredData.RoutineNamedAsync(factory, name))!.Id;
            Assert.Equal(HttpStatusCode.Redirect, (await alex.ChooseRoutineDatesAsync(routine, "2026-11-02", "2026-11-08")).StatusCode);
            return Assert.Single(await StoredData.DatesChosenForAsync(factory, routine)).Id;
        }

        throw new ArgumentOutOfRangeException(nameof(path), path, "Add a way to create Alex's record for this controller.");
    }

    // A POST carries Sam's own valid anti-forgery token (from a page Sam can open), so the request reaches the
    // ownership check instead of being turned away earlier as a forged form. It fills the fields of every form, so
    // it would pass validation if it were ever allowed to reach it. A page or form for one session of an activity
    // (CAL-4, FIT-1) is given its date, that of Alex's activity's only one, so it isn't "not found" for lack of it.
    private static Task<HttpResponseMessage> SendAsync(TestBrowser browser, string method, string url) => method switch
    {
        "GET" => browser.GetAsync($"{url}?date={SessionDate}"),
        "POST" => browser.SubmitFormAsync("/Locations/Create", url, new(TestBrowser.ActivityForm("Changed by Sam")
            .Concat(TestBrowser.GoalForm("Changed by Sam"))
            .Concat(TestBrowser.RoutineForm("Changed by Sam"))
            .Concat(TestBrowser.RoutineDatesForm(Guid.NewGuid(), "2026-12-01", "2026-12-02"))
            .DistinctBy(field => field.Key).ToDictionary())
        {
            ["Name"] = "Changed by Sam",
            ["Minutes"] = "5",
            ["Mode"] = "Walk",
            ["Date"] = SessionDate,
            ["Mark"] = "Skipped",
        }),
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Only GET and POST are used."),
    };

    // The date of Alex's activity's one session (TestBrowser.ActivityForm's start date).
    private const string SessionDate = "2026-11-02";

    // An action with no [HttpGet]/[HttpPost] answers every method; reporting "ANY" makes this test fail until
    // the action says which method it takes.
    private static IEnumerable<string> HttpMethodsOf(ControllerActionDescriptor action)
    {
        var methods = action.ActionConstraints?.OfType<HttpMethodActionConstraint>()
            .SelectMany(constraint => constraint.HttpMethods)
            .ToList() ?? [];
        return methods.Count > 0 ? methods : ["ANY"];
    }

    private static async Task<string> TitleOfAsync(HttpResponseMessage response)
    {
        var title = Title().Match(await response.Content.ReadAsStringAsync());
        Assert.True(title.Success, "The response has no <title>.");
        return title.Groups[1].Value;
    }

    [GeneratedRegex("<title>(.*?)</title>", RegexOptions.Singleline)]
    private static partial Regex Title();
}
