using Microsoft.AspNetCore.Html;

namespace FitnessTracker.Web.Models.Workouts;

/// <summary>
/// One figure on a session's page, as its tile shows it (Views/Sessions/_Figures): "Distance", "5.2" "km"; "Effort", "6"
/// "of 10", "moderate"; "Sets", "12".
/// </summary>
public sealed record SessionFigure(string Label, string Value, string? Unit = null, string? Note = null)
{
    /// <summary>
    /// "5.2 km" as the figure 5.2 and its unit, which is shown smaller. The domain writes a number, a space and the unit,
    /// and a number never has a space in it.
    /// </summary>
    public static SessionFigure WithUnit(string label, string text)
    {
        var space = text.IndexOf(' ', StringComparison.Ordinal);
        return new SessionFigure(label, text[..space], text[(space + 1)..]);
    }

    /// <summary>
    /// A figure as its tile shows it, free to break after a colon on a narrow screen ("2399:" then "58:20"), never
    /// inside a number (break-ui, Phase 7). Each part is encoded; only the break hints are markup.
    /// </summary>
    public static IHtmlContent Breakable(string figure)
    {
        var html = new HtmlContentBuilder();
        var parts = figure.Split(':');
        for (var i = 0; i < parts.Length; i++)
        {
            if (i > 0)
            {
                html.AppendHtml(":<wbr>");
            }

            html.Append(parts[i]);
        }

        return html;
    }
}
