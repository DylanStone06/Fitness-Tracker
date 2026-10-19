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
    /// with the page's hidden fields (the anti-forgery token among them), as submitting the form would.</summary>
    public async Task<HttpResponseMessage> SubmitFormAsync(string pageUrl, string actionUrl, Dictionary<string, string> fields) =>
        await SubmitAsync(await LoadFormAsync(pageUrl), actionUrl, fields);

    /// <summary>Opens a page and keeps its form, to submit later: like a form left open in a browser tab.</summary>
    public async Task<LoadedForm> LoadFormAsync(string pageUrl)
    {
        var page = await Http.GetAsync(pageUrl);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        return FormIn(await page.Content.ReadAsStringAsync(), pageUrl);
    }

    /// <summary>
    /// The hidden fields of a page's forms. Pages here have one form plus the header's sign-out form, which only
    /// adds the same anti-forgery token, so taking every hidden field on the page is enough.
    /// </summary>
    public static LoadedForm FormIn(string html, string where = "the page")
    {
        var hiddenFields = HiddenInput().Matches(html)
            .Select(input => (Name: AttributeOf(input.Value, "name"), Value: AttributeOf(input.Value, "value")))
            .Where(field => field.Name is not null)
            .GroupBy(field => field.Name!)
            .ToDictionary(fields => fields.Key, fields => fields.First().Value ?? "");
        Assert.True(hiddenFields.ContainsKey("__RequestVerificationToken"), $"No anti-forgery token on {where}.");
        return new LoadedForm(hiddenFields);
    }

    /// <summary>Posts a form loaded earlier, with <paramref name="fields"/> filled in.</summary>
    public Task<HttpResponseMessage> SubmitAsync(LoadedForm form, string actionUrl, Dictionary<string, string> fields)
    {
        var posted = new Dictionary<string, string>(form.HiddenFields);
        foreach (var (name, value) in fields)
        {
            posted[name] = value;
        }

        return Http.PostAsync(actionUrl, new FormUrlEncodedContent(posted));
    }

    private static string? AttributeOf(string tag, string attribute)
    {
        var match = Regex.Match(tag, @"\s" + attribute + "=\"([^\"]*)\"");
        return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value) : null;
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

    // ASP.NET Core renders, for example: <input name="__RequestVerificationToken" type="hidden" value="..." />
    [GeneratedRegex("""<input[^>]*\stype="hidden"[^>]*>""")]
    private static partial Regex HiddenInput();
}

/// <summary>A form as a page gave it: its hidden fields, the anti-forgery token among them.</summary>
public sealed record LoadedForm(IReadOnlyDictionary<string, string> HiddenFields);
