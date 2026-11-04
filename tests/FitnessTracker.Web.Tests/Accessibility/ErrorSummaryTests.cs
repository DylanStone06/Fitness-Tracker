using System.Net;
using System.Text.RegularExpressions;
using FitnessTracker.Web.Tests.Infrastructure;

namespace FitnessTracker.Web.Tests.Accessibility;

// WCAG 2.2 AA and the GOV.UK error pattern: each problem in the summary at the top of a form links to the field it
// is about, in the order the fields appear, so keyboard and screen reader users can jump straight to it.
public sealed partial class ErrorSummaryTests(TestAppFactory factory) : IClassFixture<TestAppFactory>
{
    [Fact]
    public async Task EachFieldsProblem_LinksToThatField()
    {
        var summary = await SummaryAfterSubmittingAnEmptyRegisterFormAsync();

        Assert.Contains("""<a href="#Email">Enter your email address.</a>""", summary);
        Assert.Contains("""<a href="#Password">Enter a password.</a>""", summary);
        Assert.Contains("""<a href="#ConfirmPassword">Enter the password again.</a>""", summary);
    }

    [Fact]
    public async Task Problems_AreInTheOrderOfTheFields()
    {
        var summary = await SummaryAfterSubmittingAnEmptyRegisterFormAsync();

        var email = summary.IndexOf("#Email", StringComparison.Ordinal);
        var password = summary.IndexOf("#Password", StringComparison.Ordinal);
        var confirmPassword = summary.IndexOf("#ConfirmPassword", StringComparison.Ordinal);
        Assert.True(email >= 0 && email < password && password < confirmPassword, $"Got {email}, {password}, {confirmPassword}.");
    }

    // "The email address or password is incorrect" is about the form as a whole, so there is no field to link to.
    [Fact]
    public async Task AProblemWithTheWholeForm_IsPlainText()
    {
        var response = await TestBrowser.Open(factory).LoginAsync(TestBrowser.NewEmail("nobody"));

        var summary = SummaryOf(await response.Content.ReadAsStringAsync());
        Assert.Contains("<li>The email address or password is incorrect.</li>", summary);
    }

    private async Task<string> SummaryAfterSubmittingAnEmptyRegisterFormAsync()
    {
        var response = await TestBrowser.Open(factory).RegisterAsync(email: "", password: "", confirmPassword: "");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return SummaryOf(await response.Content.ReadAsStringAsync());
    }

    private static string SummaryOf(string html)
    {
        var summary = Summary().Match(html);
        Assert.True(summary.Success, "The page has no error summary.");
        return summary.Groups[1].Value;
    }

    [GeneratedRegex("""<div[^>]*class="[^"]*validation-summary-errors[^"]*"[^>]*>(.*?)</div>""", RegexOptions.Singleline)]
    private static partial Regex Summary();
}
