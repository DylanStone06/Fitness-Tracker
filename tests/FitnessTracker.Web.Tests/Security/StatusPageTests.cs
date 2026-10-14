using System.Net;
using FitnessTracker.Web.Tests.Infrastructure;

namespace FitnessTracker.Web.Tests.Security;

// "Not found" (including another user's data, T19) and a rejected form keep their status codes, but show
// the app's own page explaining what to do, instead of the browser's blank error page.
public sealed class StatusPageTests(TestAppFactory factory) : IClassFixture<TestAppFactory>
{
    [Fact]
    public async Task UnknownAddress_Returns404_WithTheAppsOwnPage()
    {
        var browser = TestBrowser.Open(factory);
        await browser.RegisterAsync(TestBrowser.NewEmail("alex"));

        var response = await browser.GetAsync("/no/such/page");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("- Fitness Tracker</title>", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task RejectedForm_Returns400_WithTheAppsOwnPage()
    {
        var browser = TestBrowser.Open(factory);

        var response = await browser.Http.PostAsync("/Account/Login", new FormUrlEncodedContent([]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("- Fitness Tracker</title>", await response.Content.ReadAsStringAsync());
    }
}
