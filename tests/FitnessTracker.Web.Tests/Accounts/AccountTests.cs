using System.Net;
using System.Text.RegularExpressions;
using FitnessTracker.Web.Data;
using FitnessTracker.Web.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace FitnessTracker.Web.Tests.Accounts;

// ACC-1: register with email and password, sign in, sign out.
public sealed partial class AccountTests(TestAppFactory factory) : IClassFixture<TestAppFactory>
{
    [Fact]
    public async Task Register_CreatesTheAccountAndSignsIn_WhenDetailsAreValid()
    {
        var browser = TestBrowser.Open(factory);
        var email = TestBrowser.NewEmail("alex");

        var response = await browser.RegisterAsync(email);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
        Assert.True(await browser.IsSignedInAsync());
        Assert.NotNull(await FindUserAsync(email));
    }

    [Fact]
    public async Task Register_ShowsAnError_WhenTheEmailIsAlreadyRegistered()
    {
        var email = TestBrowser.NewEmail("alex");
        await TestBrowser.Open(factory).RegisterAsync(email);
        var someoneElse = TestBrowser.Open(factory);

        var response = await someoneElse.RegisterAsync(email);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEmpty(await ErrorsOnAsync(response));
        Assert.False(await someoneElse.IsSignedInAsync());
    }

    [Fact]
    public async Task Register_ShowsAnError_WhenThePasswordIsShorterThanTwelveCharacters()
    {
        var browser = TestBrowser.Open(factory);
        var email = TestBrowser.NewEmail("alex");

        var response = await browser.RegisterAsync(email, password: "elevenchars");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEmpty(await ErrorsOnAsync(response));
        Assert.Null(await FindUserAsync(email));
    }

    [Fact]
    public async Task Register_Succeeds_WithALongPasswordThatHasNoDigitsCapitalsOrSymbols()
    {
        var browser = TestBrowser.Open(factory);
        var email = TestBrowser.NewEmail("alex");

        var response = await browser.RegisterAsync(email, password: "plain lowercase words");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(await FindUserAsync(email));
    }

    [Fact]
    public async Task Register_ShowsAnError_WhenThePasswordsDoNotMatch()
    {
        var browser = TestBrowser.Open(factory);
        var email = TestBrowser.NewEmail("alex");

        var response = await browser.RegisterAsync(email, confirmPassword: TestBrowser.Password + " typo");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEmpty(await ErrorsOnAsync(response));
        Assert.Null(await FindUserAsync(email));
    }

    // Screen reader users hear the page title first, so it must say there is a problem.
    [Fact]
    public async Task Register_PageTitleStartsWithError_WhenThereIsAProblem()
    {
        var response = await TestBrowser.Open(factory).RegisterAsync("not-an-email-address");

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("<title>Error: ", html);
    }

    // Phone keyboards add a space after an autocompleted address.
    [Fact]
    public async Task RegisterAndLogin_IgnoreSpacesAroundTheEmail()
    {
        var email = TestBrowser.NewEmail("alex");
        var registered = await TestBrowser.Open(factory).RegisterAsync($" {email} ");
        var browser = TestBrowser.Open(factory);

        var response = await browser.LoginAsync($"{email} ");

        Assert.Equal(HttpStatusCode.Redirect, registered.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.True(await browser.IsSignedInAsync());
        Assert.NotNull(await FindUserAsync(email));
    }

    [Fact]
    public async Task Login_SignsIn_WithTheCorrectPassword()
    {
        var email = TestBrowser.NewEmail("sam");
        await TestBrowser.Open(factory).RegisterAsync(email);
        var browser = TestBrowser.Open(factory);

        var response = await browser.LoginAsync(email);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.True(await browser.IsSignedInAsync());
    }

    // The sign-in page must not reveal which email addresses have accounts.
    [Fact]
    public async Task Login_ShowsTheSameError_ForAWrongPasswordAndAnUnknownEmail()
    {
        var email = TestBrowser.NewEmail("sam");
        await TestBrowser.Open(factory).RegisterAsync(email);

        var wrongPassword = await TestBrowser.Open(factory).LoginAsync(email, password: "not the right passphrase");
        var unknownEmail = await TestBrowser.Open(factory).LoginAsync(TestBrowser.NewEmail("nobody"));

        Assert.Equal(HttpStatusCode.OK, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.OK, unknownEmail.StatusCode);
        var wrongPasswordErrors = await ErrorsOnAsync(wrongPassword);
        Assert.NotEmpty(wrongPasswordErrors);
        Assert.Equal(wrongPasswordErrors, await ErrorsOnAsync(unknownEmail));
    }

    [Fact]
    public async Task Login_RefusesEvenTheCorrectPassword_AfterFiveFailedAttempts()
    {
        var email = TestBrowser.NewEmail("sam");
        await TestBrowser.Open(factory).RegisterAsync(email);
        var browser = TestBrowser.Open(factory);
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            await browser.LoginAsync(email, password: "not the right passphrase");
        }

        var response = await browser.LoginAsync(email);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(await browser.IsSignedInAsync());
    }

    [Fact]
    public async Task Login_ReturnsToTheRequestedPage_WhenItIsOnThisSite()
    {
        var email = TestBrowser.NewEmail("sam");
        await TestBrowser.Open(factory).RegisterAsync(email);

        var response = await TestBrowser.Open(factory).LoginAsync(email, returnUrl: "/Home/Index");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Home/Index", response.Headers.Location?.OriginalString);
    }

    // Stops a link like /Account/Login?returnUrl=https://evil.example.com sending people elsewhere after signing in.
    [Theory]
    [InlineData("https://evil.example.com/")]
    [InlineData("//evil.example.com/")]
    public async Task Login_GoesHome_WhenTheReturnUrlPointsToAnotherSite(string returnUrl)
    {
        var email = TestBrowser.NewEmail("sam");
        await TestBrowser.Open(factory).RegisterAsync(email);

        var response = await TestBrowser.Open(factory).LoginAsync(email, returnUrl: returnUrl);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Logout_EndsTheSession()
    {
        var browser = TestBrowser.Open(factory);
        await browser.RegisterAsync(TestBrowser.NewEmail("alex"));

        var response = await browser.LogoutAsync();

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.False(await browser.IsSignedInAsync());
    }

    // A plain link or image can't sign someone out: only a form post with a token can.
    [Fact]
    public async Task Logout_DoesNothing_ForAGetRequest()
    {
        var browser = TestBrowser.Open(factory);
        await browser.RegisterAsync(TestBrowser.NewEmail("alex"));

        await browser.GetAsync("/Account/Logout");

        Assert.True(await browser.IsSignedInAsync());
    }

    private async Task<AppUser?> FindUserAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().FindByEmailAsync(email);
    }

    /// <summary>The messages listed in the page's error summary, without their links (empty when there is none).</summary>
    private static async Task<List<string>> ErrorsOnAsync(HttpResponseMessage response)
    {
        var html = await response.Content.ReadAsStringAsync();
        var summary = ErrorSummary().Match(html);
        if (!summary.Success)
        {
            return [];
        }

        return ListItem().Matches(summary.Groups[1].Value)
            .Select(item => Tag().Replace(item.Groups[1].Value, "").Trim())
            .Where(message => message.Length > 0)
            .ToList();
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tag();

    [GeneratedRegex("""<div[^>]*class="[^"]*validation-summary-errors[^"]*"[^>]*>(.*?)</div>""", RegexOptions.Singleline)]
    private static partial Regex ErrorSummary();

    [GeneratedRegex("<li>(.*?)</li>", RegexOptions.Singleline)]
    private static partial Regex ListItem();
}
