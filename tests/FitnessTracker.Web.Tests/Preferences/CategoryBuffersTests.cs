using System.Net;
using System.Text.RegularExpressions;
using FitnessTracker.Web.Data;
using FitnessTracker.Web.Tests.Infrastructure;

namespace FitnessTracker.Web.Tests.Preferences;

// ACC-6: default getting-ready and changing times per category of activity. The page lists every category in the
// order of ActivityCategory (Work, Commute, Gym, Run, ...), so Gym is row 2 and Run row 3.
public sealed class CategoryBuffersTests(TestAppFactory factory) : IClassFixture<TestAppFactory>
{
    private const string Page = "/CategoryBuffers";

    [Fact]
    public async Task ANewUser_SeesTheBuiltInDefaults()
    {
        var (browser, _) = await SignedInAsync("alex");

        var html = await (await browser.GetAsync(Page)).Content.ReadAsStringAsync();

        Assert.Equal(("10", "10"), (InputValue(html, "Rows[2].PreparationMinutes"), InputValue(html, "Rows[2].ChangingMinutes")));
        Assert.Equal(("0", "0"), (InputValue(html, "Rows[0].PreparationMinutes"), InputValue(html, "Rows[0].ChangingMinutes")));
    }

    [Fact]
    public async Task Saving_StoresEachCategorysDefaults_AndShowsThem()
    {
        var (browser, email) = await SignedInAsync("alex");

        var response = await browser.SubmitFormAsync(Page, Page, Form(new() { ["Rows[3].PreparationMinutes"] = "7", ["Rows[3].ChangingMinutes"] = "12" }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var stored = await StoredData.CategoryBuffersOfAsync(factory, email);
        Assert.Equal((7, 12), (stored[ActivityCategory.Run].PreparationMinutes, stored[ActivityCategory.Run].ChangingMinutes));
        Assert.Equal("7", InputValue(await (await browser.GetAsync(Page)).Content.ReadAsStringAsync(), "Rows[3].PreparationMinutes"));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("241")]
    public async Task Saving_ShowsAnErrorLinkedToTheField_AndSavesNothing_ForMinutesOutOfRange(string minutes)
    {
        var (browser, email) = await SignedInAsync("alex");

        var response = await browser.SubmitFormAsync(Page, Page, Form(new() { ["Rows[2].PreparationMinutes"] = minutes }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("<title>Error: ", html);
        Assert.Contains("href=\"#Rows_2__PreparationMinutes\"", html);
        Assert.Empty(await StoredData.CategoryBuffersOfAsync(factory, email));
    }

    // Requirements §9: a form left open in another tab can't silently overwrite a newer save.
    [Fact]
    public async Task Saving_FromAnOutOfDateTab_IsNotSaved_AndSaysWhy()
    {
        var (browser, email) = await SignedInAsync("alex");
        await browser.SubmitFormAsync(Page, Page, Form([]));
        var oldTab = await browser.LoadFormAsync(Page);
        await browser.SubmitFormAsync(Page, Page, Form(new() { ["Rows[3].ChangingMinutes"] = "20" }));

        var response = await browser.SubmitAsync(oldTab, Page, Form(new() { ["Rows[3].ChangingMinutes"] = "30" }));

        Assert.Contains("changed in another tab", await response.Content.ReadAsStringAsync());
        Assert.Equal(20, (await StoredData.CategoryBuffersOfAsync(factory, email))[ActivityCategory.Run].ChangingMinutes);
    }

    // T19: the page has no id in its address; it only ever shows and changes the signed-in user's own defaults.
    [Fact]
    public async Task AnotherUsersDefaults_AreLeftAlone_AndNotShown()
    {
        var (alex, alexsEmail) = await SignedInAsync("alex");
        await alex.SubmitFormAsync(Page, Page, Form(new() { ["Rows[2].PreparationMinutes"] = "30" }));
        var (sam, samsEmail) = await SignedInAsync("sam");

        var samsPage = await (await sam.GetAsync(Page)).Content.ReadAsStringAsync();
        await sam.SubmitFormAsync(Page, Page, Form(new() { ["Rows[2].PreparationMinutes"] = "5" }));

        Assert.Equal("10", InputValue(samsPage, "Rows[2].PreparationMinutes"));
        Assert.Equal(30, (await StoredData.CategoryBuffersOfAsync(factory, alexsEmail))[ActivityCategory.Gym].PreparationMinutes);
        Assert.Equal(5, (await StoredData.CategoryBuffersOfAsync(factory, samsEmail))[ActivityCategory.Gym].PreparationMinutes);
    }

    private static Dictionary<string, string> Form(Dictionary<string, string> changes) => TestBrowser.CategoryBuffersForm(changes);

    private async Task<(TestBrowser Browser, string Email)> SignedInAsync(string name)
    {
        var browser = TestBrowser.Open(factory);
        var email = TestBrowser.NewEmail(name);
        Assert.Equal(HttpStatusCode.Redirect, (await browser.RegisterAsync(email)).StatusCode);
        return (browser, email);
    }

    private static string? InputValue(string html, string name)
    {
        var input = Regex.Match(html, $"""<input[^>]*\sname="{Regex.Escape(name)}"[^>]*>""");
        var value = Regex.Match(input.Value, """\svalue="([^"]*)" """.TrimEnd());
        return value.Success ? WebUtility.HtmlDecode(value.Groups[1].Value) : null;
    }
}
