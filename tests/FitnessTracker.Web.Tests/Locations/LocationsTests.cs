using System.Net;
using FitnessTracker.Web.Tests.Infrastructure;

namespace FitnessTracker.Web.Tests.Locations;

// TRV-1, first version: a signed-in user adds, renames and deletes their own locations. Phase 2 stores only the
// name; opening hours and travel legs arrive in Phase 4. Another user's locations are covered by T19, in
// Security/AccessControlTests.
public sealed class LocationsTests(TestAppFactory factory) : IClassFixture<TestAppFactory>
{
    [Fact]
    public async Task Add_SavesTheLocation_AndReturnsToTheList()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var name = TestBrowser.NewName("Home gym");

        var response = await browser.AddLocationAsync(name);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Locations", response.Headers.Location?.OriginalString);
        Assert.Contains(name, await PageAsync(browser, "/Locations"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Add_ShowsAnError_AndSavesNothing_WhenTheNameIsBlank(string name)
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var countBefore = await StoredData.LocationCountAsync(factory);

        var response = await browser.AddLocationAsync(name);

        await AssertShowsAnErrorAsync(response);
        Assert.Equal(countBefore, await StoredData.LocationCountAsync(factory));
    }

    // The database column holds 100 characters.
    [Fact]
    public async Task Add_ShowsAnError_AndSavesNothing_WhenTheNameIsLongerThan100Characters()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var countBefore = await StoredData.LocationCountAsync(factory);

        var response = await browser.AddLocationAsync(new string('x', 101));

        await AssertShowsAnErrorAsync(response);
        Assert.Equal(countBefore, await StoredData.LocationCountAsync(factory));
    }

    // A browser's maxlength cuts a pasted longer name short without saying so (GOV.UK advises against it); the
    // server's check above gives a clear error instead.
    [Fact]
    public async Task AddPage_DoesNotSilentlyCutLongNamesShort()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");

        Assert.DoesNotContain("maxlength", await PageAsync(browser, "/Locations/Create"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Add_Accepts_ANameOfExactly100Characters()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var name = TestBrowser.NewName("Gym").PadRight(100, 'x');

        var response = await browser.AddLocationAsync(name);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(await StoredData.LocationNamedAsync(factory, name));
    }

    // Phone keyboards add a space after an autocompleted word.
    [Fact]
    public async Task Add_IgnoresSpacesAroundTheName()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var name = TestBrowser.NewName("Office");

        await browser.AddLocationAsync($"  {name} ");

        Assert.NotNull(await StoredData.LocationNamedAsync(factory, name));
    }

    // Added out of order, with capitals that a plain character-code sort would put first ("Beta" before "alpha").
    [Fact]
    public async Task List_IsInAlphabeticalOrder_IgnoringCapitals()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        await browser.AddLocationAsync("zeta park");
        await browser.AddLocationAsync("alpha studio");
        await browser.AddLocationAsync("Beta pool");

        var html = await PageAsync(browser, "/Locations");

        var alpha = html.IndexOf("alpha studio", StringComparison.Ordinal);
        var beta = html.IndexOf("Beta pool", StringComparison.Ordinal);
        var zeta = html.IndexOf("zeta park", StringComparison.Ordinal);
        Assert.True(alpha >= 0 && alpha < beta && beta < zeta, $"Expected alpha < Beta < zeta, got {alpha}, {beta}, {zeta}.");
    }

    [Fact]
    public async Task EditPage_ShowsTheCurrentName()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var name = TestBrowser.NewName("Home gym");
        var id = await AddAsync(browser, name);

        var html = await PageAsync(browser, $"/Locations/Edit/{id}");

        Assert.Contains($"value=\"{name}\"", html);
    }

    [Fact]
    public async Task Edit_SavesTheNewName_AndReturnsToTheList()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var id = await AddAsync(browser, TestBrowser.NewName("Home gym"));
        var newName = TestBrowser.NewName("Town gym");

        var response = await browser.SubmitFormAsync($"/Locations/Edit/{id}", $"/Locations/Edit/{id}", new() { ["Name"] = newName });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Locations", response.Headers.Location?.OriginalString);
        Assert.Equal(id, (await StoredData.LocationNamedAsync(factory, newName))?.Id);
    }

    [Fact]
    public async Task Edit_ShowsAnError_AndKeepsTheOldName_WhenTheNewNameIsBlank()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var name = TestBrowser.NewName("Home gym");
        var id = await AddAsync(browser, name);

        var response = await browser.SubmitFormAsync($"/Locations/Edit/{id}", $"/Locations/Edit/{id}", new() { ["Name"] = " " });

        await AssertShowsAnErrorAsync(response);
        Assert.NotNull(await StoredData.LocationNamedAsync(factory, name));
    }

    // Opening the delete page only asks; a link, a prefetch or a crawler must never delete anything.
    [Fact]
    public async Task DeletePage_AsksFirst_AndDeletesNothing()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var name = TestBrowser.NewName("Home gym");
        var id = await AddAsync(browser, name);

        var html = await PageAsync(browser, $"/Locations/Delete/{id}");

        Assert.Contains(name, html);
        Assert.NotNull(await StoredData.LocationNamedAsync(factory, name));
    }

    [Fact]
    public async Task Delete_RemovesTheLocation_AndReturnsToTheList()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var name = TestBrowser.NewName("Home gym");
        var id = await AddAsync(browser, name);

        var response = await browser.SubmitFormAsync($"/Locations/Delete/{id}", $"/Locations/Delete/{id}", []);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Locations", response.Headers.Location?.OriginalString);
        Assert.Null(await StoredData.LocationNamedAsync(factory, name));
    }

    private async Task<Guid> AddAsync(TestBrowser browser, string name)
    {
        var response = await browser.AddLocationAsync(name);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return (await StoredData.LocationNamedAsync(factory, name))!.Id;
    }

    private static async Task<string> PageAsync(TestBrowser browser, string url)
    {
        var response = await browser.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>The form is shown again (not saved and redirected), titled "Error:" for screen readers.</summary>
    private static async Task AssertShowsAnErrorAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<title>Error: ", await response.Content.ReadAsStringAsync());
    }
}
