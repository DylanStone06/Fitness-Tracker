using System.Net;
using FitnessTracker.Web.Tests.Infrastructure;

namespace FitnessTracker.Web.Tests.Security;

// ACC-2: every page except sign-in and register needs a signed-in user.
public sealed class SignInRequiredTests(TestAppFactory factory) : IClassFixture<TestAppFactory>
{
    [Theory]
    [InlineData("/")]
    [InlineData("/Home/Index")]
    [InlineData("/Locations")]
    [InlineData("/Locations/Create")]
    [InlineData("/Calendar/Events?start=2026-10-19&end=2026-10-26")]
    public async Task Page_RedirectsToSignIn_WhenSignedOut(string url)
    {
        var browser = TestBrowser.Open(factory);

        var response = await browser.GetAsync(url);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Login", response.Headers.Location?.AbsolutePath);
    }

    [Theory]
    [InlineData("/Account/Login")]
    [InlineData("/Account/Register")]
    [InlineData("/Home/Error")]
    [InlineData("/css/site.css")]
    [InlineData("/js/site.js")]
    [InlineData("/lib/bootstrap/dist/css/bootstrap.min.css")]
    [InlineData("/lib/fullcalendar/index.global.min.js")]
    public async Task PublicPageOrFile_Opens_WhenSignedOut(string url)
    {
        var browser = TestBrowser.Open(factory);

        var response = await browser.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
