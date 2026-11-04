using Microsoft.AspNetCore.Razor.TagHelpers;

namespace FitnessTracker.Web.Infrastructure;

/// <summary>
/// Removes the maxlength that ASP.NET Core adds to form fields from a [StringLength] or [MaxLength] rule. A browser
/// with maxlength cuts a pasted longer value short without saying so, which GOV.UK's guidance warns against; the
/// server's own length check shows a clear error instead. A maxlength written by hand on an asp-for field is
/// removed too, on purpose: limits belong in the model's rules, where the server checks them.
/// </summary>
[HtmlTargetElement("input", Attributes = "asp-for")]
[HtmlTargetElement("textarea", Attributes = "asp-for")]
public sealed class NoMaxLengthTagHelper : TagHelper
{
    // After ASP.NET Core's own input and textarea tag helpers, which add the attribute.
    public override int Order => int.MaxValue;

    public override void Process(TagHelperContext context, TagHelperOutput output) => output.Attributes.RemoveAll("maxlength");
}
