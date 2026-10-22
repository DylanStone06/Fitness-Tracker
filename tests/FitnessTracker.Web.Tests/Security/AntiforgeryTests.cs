using System.Net;
using FitnessTracker.Web.Tests.Infrastructure;

namespace FitnessTracker.Web.Tests.Security;

// Requirements §9: anti-forgery tokens on every form, so another site can't submit forms as the user.
public sealed class AntiforgeryTests(TestAppFactory factory) : IClassFixture<TestAppFactory>
{
    [Theory]
    [InlineData("/Account/Login")]
    [InlineData("/Account/Register")]
    public async Task Post_IsRejected_WithoutAnAntiforgeryToken(string url)
    {
        var browser = TestBrowser.Open(factory);
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = TestBrowser.NewEmail("forged"),
            ["Password"] = TestBrowser.Password,
            ["ConfirmPassword"] = TestBrowser.Password,
        });

        var response = await browser.Http.PostAsync(url, form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // The id is made up: a forged form is turned away before anything is looked up.
    [Theory]
    [InlineData("/Locations/Create")]
    [InlineData("/Locations/Edit/0199e3a0-0000-7000-8000-000000000000")]
    [InlineData("/Locations/Delete/0199e3a0-0000-7000-8000-000000000000")]
    public async Task SignedInPost_IsRejected_WithoutAnAntiforgeryToken(string url)
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "sam");
        var countBefore = await StoredData.LocationCountAsync(factory);

        var response = await browser.Http.PostAsync(url, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Name"] = "Forged",
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(countBefore, await StoredData.LocationCountAsync(factory));
    }

    [Fact]
    public async Task Logout_IsRejected_WithoutAnAntiforgeryToken()
    {
        var browser = TestBrowser.Open(factory);
        await browser.RegisterAsync(TestBrowser.NewEmail("sam"));

        var response = await browser.Http.PostAsync("/Account/Logout", new FormUrlEncodedContent([]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(await browser.IsSignedInAsync());
    }
}
