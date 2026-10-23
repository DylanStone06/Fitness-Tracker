using System.Net;
using FitnessTracker.Web.Tests.Infrastructure;

namespace FitnessTracker.Web.Tests.Security;

// Requirements §9 (privacy and security), from the security review of 14 October 2026: every response tells the
// browser not to let other sites frame the app (clickjacking), not to guess content types, to run only this site's
// own scripts and styles, and not to send page addresses to other sites. Pages, which hold personal data, aren't
// stored by the browser, so Back after signing out on a shared computer can't show them; CSS and scripts still are.
public sealed class SecurityHeadersTests(TestAppFactory factory) : IClassFixture<TestAppFactory>
{
    [Theory]
    [InlineData("/Account/Login", HttpStatusCode.OK)]
    [InlineData("/Locations", HttpStatusCode.Redirect)] // to sign in
    public async Task SignedOutResponses_CarryTheSecurityHeaders(string url, HttpStatusCode expectedStatus)
    {
        var response = await TestBrowser.Open(factory).GetAsync(url);

        Assert.Equal(expectedStatus, response.StatusCode);
        AssertSecurityHeaders(response);
    }

    [Theory]
    [InlineData("/Locations", HttpStatusCode.OK)]
    [InlineData("/no/such/page", HttpStatusCode.NotFound)] // the app's own "not found" page
    public async Task SignedInPages_CarryTheSecurityHeaders_AndAreNotStored(string url, HttpStatusCode expectedStatus)
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");

        var response = await browser.GetAsync(url);

        Assert.Equal(expectedStatus, response.StatusCode);
        AssertSecurityHeaders(response);
        Assert.True(response.Headers.CacheControl?.NoStore, "A page with personal data must be sent with Cache-Control: no-store.");
    }

    [Fact]
    public async Task StyleSheets_CanStillBeCached()
    {
        var response = await TestBrowser.Open(factory).GetAsync("/css/site.css");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.CacheControl?.NoStore ?? false);
    }

    private static void AssertSecurityHeaders(HttpResponseMessage response)
    {
        Assert.Equal("nosniff", Single(response, "X-Content-Type-Options"));
        Assert.Equal("DENY", Single(response, "X-Frame-Options"));
        Assert.Equal("same-origin", Single(response, "Referrer-Policy"));

        var policy = Single(response, "Content-Security-Policy");
        Assert.Contains("default-src 'self'", policy);
        Assert.Contains("frame-ancestors 'none'", policy);
        Assert.Contains("object-src 'none'", policy);
        Assert.DoesNotContain("unsafe-inline", policy);
        Assert.DoesNotContain("unsafe-eval", policy);
    }

    private static string Single(HttpResponseMessage response, string header)
    {
        Assert.True(response.Headers.TryGetValues(header, out var values), $"No {header} header.");
        return Assert.Single(values);
    }
}
