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
    };

    [Theory]
    [MemberData(nameof(ActionsThatTakeAnId))]
    public async Task AnotherUsersLocation_IsNotFound_AndLeftAlone(string method, string path)
    {
        var (alexsGym, name) = await AlexAddsALocationAsync();
        var sam = await TestBrowser.OpenSignedInAsync(factory, "sam");

        var response = await SendAsync(sam, method, path.Replace("{id}", alexsGym.ToString()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(alexsGym, (await StoredData.LocationNamedAsync(factory, name))?.Id);
    }

    [Theory]
    [MemberData(nameof(ActionsThatTakeAnId))]
    public async Task AnotherUsersLocation_LooksTheSameAsOneThatDoesNotExist(string method, string path)
    {
        var (alexsGym, _) = await AlexAddsALocationAsync();
        var sam = await TestBrowser.OpenSignedInAsync(factory, "sam");

        var forAlexsGym = await SendAsync(sam, method, path.Replace("{id}", alexsGym.ToString()));
        var forNoSuchLocation = await SendAsync(sam, method, path.Replace("{id}", Guid.NewGuid().ToString()));

        Assert.Equal(forNoSuchLocation.StatusCode, forAlexsGym.StatusCode);
        Assert.Equal(await TitleOfAsync(forNoSuchLocation), await TitleOfAsync(forAlexsGym));
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

    private async Task<(Guid Id, string Name)> AlexAddsALocationAsync()
    {
        var alex = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var name = TestBrowser.NewName("Alex gym");
        var added = await alex.AddLocationAsync(name);
        Assert.Equal(HttpStatusCode.Redirect, added.StatusCode);
        return ((await StoredData.LocationNamedAsync(factory, name))!.Id, name);
    }

    // A POST carries Sam's own valid anti-forgery token (from a page Sam can open), so the request reaches the
    // ownership check instead of being turned away earlier as a forged form.
    private static Task<HttpResponseMessage> SendAsync(TestBrowser browser, string method, string url) => method switch
    {
        "GET" => browser.GetAsync(url),
        "POST" => browser.SubmitFormAsync("/Locations/Create", url, new() { ["Name"] = "Changed by Sam" }),
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Only GET and POST are used."),
    };

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
