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

    public static TestBrowser Open(WebApplicationFactory<Program> factory) =>
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

    public Task<HttpResponseMessage> AddLocationAsync(string name, Dictionary<string, string>? fields = null) =>
        SubmitFormAsync("/Locations/Create", "/Locations/Create", new(fields ?? []) { ["Name"] = name });

    public Task<HttpResponseMessage> AddTravelLegAsync(Guid from, Guid to, int minutes, bool alsoTheReturnJourney = false, string mode = "Walk") =>
        SubmitFormAsync("/TravelLegs/Create", "/TravelLegs/Create", new()
        {
            ["FromLocationId"] = from.ToString(),
            ["ToLocationId"] = to.ToString(),
            ["Minutes"] = minutes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Mode"] = mode,
            ["AlsoAddReturnJourney"] = alsoTheReturnJourney ? "true" : "false",
        });

    /// <summary>
    /// Adds an activity: a one-off, one-hour gym session at 07:00 on Monday 2 November 2026 unless
    /// <paramref name="changes"/> says otherwise. Every field is filled, as the page would send them.
    /// </summary>
    public Task<HttpResponseMessage> AddActivityAsync(string title, Dictionary<string, string>? changes = null) =>
        SubmitFormAsync("/Activities/Create", "/Activities/Create", ActivityForm(title, changes));

    /// <summary>Every field of the activity form, filled in as for <see cref="AddActivityAsync"/>, with the given changes.</summary>
    public static Dictionary<string, string> ActivityForm(string title, Dictionary<string, string>? changes = null)
    {
        var form = new Dictionary<string, string>
        {
            ["Title"] = title,
            ["Category"] = "Gym",
            ["LocationId"] = "",
            ["StartDate"] = "2026-11-02",
            ["StartTime"] = "07:00",
            ["DurationHours"] = "1",
            ["DurationMinutes"] = "0",
            ["IsFixed"] = "true",
            ["Priority"] = "Normal",
            ["IsDemanding"] = "false",
            ["PreparationMinutes"] = "0",
            ["ChangingMinutes"] = "0",
            ["Notes"] = "",
            ["Repeats"] = "Never",
            ["RepeatEvery"] = "1",
            ["Ends"] = "Never",
            ["EndDate"] = "",
            ["EndCount"] = "",
        };
        foreach (var (name, value) in changes ?? [])
        {
            form[name] = value;
        }

        return form;
    }

    /// <summary>Saves every preference as a new user has them, with the given changes.</summary>
    public Task<HttpResponseMessage> SavePreferencesAsync(Dictionary<string, string> changes)
    {
        var form = new Dictionary<string, string>
        {
            ["TimeZoneId"] = "Europe/London",
            ["WeekStart"] = "Monday",
            ["Use24HourClock"] = "true",
            ["DistanceUnit"] = "Kilometres",
            ["WeightUnit"] = "Kilograms",
            ["TargetSleepHours"] = "8",
            ["MinimumSleepHours"] = "7",
            ["UsualBedtime"] = "23:00",
            ["UsualWakeTime"] = "07:00",
            ["WindDownMinutes"] = "30",
            ["WakeUpRoutineMinutes"] = "30",
            ["PreferredTimeOfDay"] = "Any",
            ["MinimumHoursBetweenDemandingSessions"] = "24",
            ["MaximumDemandingSessionsPerDay"] = "1",
        };
        foreach (var (name, value) in changes)
        {
            form[name] = value;
        }

        return SubmitFormAsync("/Preferences", "/Preferences", form);
    }

    /// <summary>
    /// GOL-1: adds a weekly goal: three one-hour gym sessions a week, nowhere in particular, unless
    /// <paramref name="changes"/> says otherwise.
    /// </summary>
    public Task<HttpResponseMessage> AddGoalAsync(string title, Dictionary<string, string>? changes = null) =>
        SubmitFormAsync("/Goals/Create", "/Goals/Create", GoalForm(title, changes));

    /// <summary>Every field of the weekly goal form, filled in as for <see cref="AddGoalAsync"/>, with the given changes.</summary>
    public static Dictionary<string, string> GoalForm(string title, Dictionary<string, string>? changes = null)
    {
        var form = new Dictionary<string, string>
        {
            ["Title"] = title,
            ["Category"] = "Gym",
            ["LocationId"] = "",
            ["SessionsPerWeek"] = "3",
            ["DurationHours"] = "1",
            ["DurationMinutes"] = "0",
            ["PreparationMinutes"] = "",
            ["ChangingMinutes"] = "",
            ["Priority"] = "Normal",
            ["IsDemanding"] = "false",
            ["PreferredTimeOfDay"] = "Any",
        };
        foreach (var (name, value) in changes ?? [])
        {
            form[name] = value;
        }

        return form;
    }

    /// <summary>
    /// CAL-9: adds a routine: one week that repeats, with week A this week (Monday 19 October 2026 in tests), not the
    /// usual one, unless <paramref name="changes"/> says otherwise.
    /// </summary>
    public Task<HttpResponseMessage> AddRoutineAsync(string name, Dictionary<string, string>? changes = null) =>
        SubmitFormAsync("/Routines/Create", "/Routines/Create", RoutineForm(name, changes));

    /// <summary>Every field of the routine form, filled in as for <see cref="AddRoutineAsync"/>, with the given changes.</summary>
    public static Dictionary<string, string> RoutineForm(string name, Dictionary<string, string>? changes = null)
    {
        var form = new Dictionary<string, string>
        {
            ["Name"] = name,
            ["CycleWeeks"] = "1",
            ["WeekA"] = "2026-10-19",
            ["IsUsual"] = "false",
        };
        foreach (var (field, value) in changes ?? [])
        {
            form[field] = value;
        }

        return form;
    }

    /// <summary>CAL-9: chooses <paramref name="routine"/> for the dates from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public Task<HttpResponseMessage> ChooseRoutineDatesAsync(Guid routine, string from, string to) =>
        SubmitFormAsync("/RoutineDates/Create", "/RoutineDates/Create", RoutineDatesForm(routine, from, to));

    /// <summary>Every field of the form that chooses a routine for some dates.</summary>
    public static Dictionary<string, string> RoutineDatesForm(Guid routine, string from, string to) => new()
    {
        ["RoutineId"] = routine.ToString(),
        ["From"] = from,
        ["To"] = to,
    };

    /// <summary>
    /// Every row of the default-buffers form (ACC-6) as a new user's page fills it (the built-in defaults), with the
    /// given changes. Rows are in ActivityCategory order: Gym is row 2, Run row 3.
    /// </summary>
    public static Dictionary<string, string> CategoryBuffersForm(Dictionary<string, string>? changes = null)
    {
        var form = new Dictionary<string, string>();
        foreach (var (category, i) in Enum.GetValues<FitnessTracker.Web.Data.ActivityCategory>().Select((category, i) => (category, i)))
        {
            var (preparation, changing) = FitnessTracker.Web.Data.CategoryBuffers.BuiltIn(category);
            form[$"Rows[{i}].PreparationMinutes"] = preparation.ToString(System.Globalization.CultureInfo.InvariantCulture);
            form[$"Rows[{i}].ChangingMinutes"] = changing.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        foreach (var (name, value) in changes ?? [])
        {
            form[name] = value;
        }

        return form;
    }

    /// <summary>True when the home page opens instead of redirecting to sign-in.</summary>
    public async Task<bool> IsSignedInAsync() =>
        (await Http.GetAsync("/")).StatusCode == HttpStatusCode.OK;

    // ASP.NET Core renders, for example: <input name="__RequestVerificationToken" type="hidden" value="..." />
    [GeneratedRegex("""<input[^>]*\stype="hidden"[^>]*>""")]
    private static partial Regex HiddenInput();
}

/// <summary>A form as a page gave it: its hidden fields, the anti-forgery token among them.</summary>
public sealed record LoadedForm(IReadOnlyDictionary<string, string> HiddenFields);
