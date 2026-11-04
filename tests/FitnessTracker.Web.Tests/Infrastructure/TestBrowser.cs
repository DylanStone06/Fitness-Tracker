using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FitnessTracker.Web.Tests.Infrastructure;

/// <summary>
/// Drives the app the way a browser would: keeps its own cookies, and submits forms with the
/// anti-forgery token the page gave it. Redirects are not followed, so tests can check where they go.
/// </summary>
public sealed partial class TestBrowser
{
    /// <summary>Long enough for the password rules; test-only, never a real password.</summary>
    public const string Password = "a long test passphrase";

    private TestBrowser(HttpClient http) => Http = http;

    public HttpClient Http { get; }

    public static TestBrowser Open(TestAppFactory factory) =>
        new(factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }));

    /// <summary>Opens a browser signed in to a new account.</summary>
    public static async Task<TestBrowser> OpenSignedInAsync(TestAppFactory factory, string name)
    {
        var browser = Open(factory);
        var registered = await browser.RegisterAsync(NewEmail(name));
        Assert.Equal(HttpStatusCode.Redirect, registered.StatusCode);
        return browser;
    }

    /// <summary>A made-up, unique address, so tests sharing a database never collide.</summary>
    public static string NewEmail(string name) => $"{name}-{Guid.NewGuid():N}@example.com";

    /// <summary>A unique name for a record, so a test can find exactly its own row in a shared database.</summary>
    public static string NewName(string label) => $"{label} {Guid.NewGuid():N}";

    public Task<HttpResponseMessage> GetAsync(string url) => Http.GetAsync(url);

    /// <summary>Loads <paramref name="pageUrl"/>, then posts <paramref name="fields"/> to <paramref name="actionUrl"/>
    /// with the page's anti-forgery token, as submitting the form would.</summary>
    public async Task<HttpResponseMessage> SubmitFormAsync(string pageUrl, string actionUrl, Dictionary<string, string> fields)
    {
        var page = await Http.GetAsync(pageUrl);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();

        var token = AntiforgeryToken().Match(html);
        Assert.True(token.Success, $"No anti-forgery token on {pageUrl}.");

        var form = new Dictionary<string, string>(fields) { ["__RequestVerificationToken"] = token.Groups[1].Value };
        return await Http.PostAsync(actionUrl, new FormUrlEncodedContent(form));
    }

    public Task<HttpResponseMessage> RegisterAsync(string email, string password = Password, string? confirmPassword = null) =>
        SubmitFormAsync("/Account/Register", "/Account/Register", new()
        {
            ["Email"] = email,
            ["Password"] = password,
            ["ConfirmPassword"] = confirmPassword ?? password,
        });

    public Task<HttpResponseMessage> LoginAsync(string email, string password = Password, string returnUrl = "") =>
        SubmitFormAsync("/Account/Login", $"/Account/Login?returnUrl={Uri.EscapeDataString(returnUrl)}", new()
        {
            ["Email"] = email,
            ["Password"] = password,
        });

    public Task<HttpResponseMessage> LogoutAsync() =>
        SubmitFormAsync("/", "/Account/Logout", []);

    public Task<HttpResponseMessage> AddLocationAsync(string name) =>
        SubmitFormAsync("/Locations/Create", "/Locations/Create", new() { ["Name"] = name });

    /// <summary>True when the home page opens instead of redirecting to sign-in.</summary>
    public async Task<bool> IsSignedInAsync() =>
        (await Http.GetAsync("/")).StatusCode == HttpStatusCode.OK;

    // ASP.NET Core renders: <input name="__RequestVerificationToken" type="hidden" value="..." />
    [GeneratedRegex("""name="__RequestVerificationToken"[^>]*value="([^"]+)"[^>]*>""")]
    private static partial Regex AntiforgeryToken();
}
