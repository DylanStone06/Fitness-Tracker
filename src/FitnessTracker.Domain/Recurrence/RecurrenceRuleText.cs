using System.Globalization;
using NodaTime;
using NodaTime.Text;

namespace FitnessTracker.Domain.Recurrence;

/// <summary>
/// ADR 0001: a <see cref="RecurrenceRule"/> stored as iCalendar RRULE text (RFC 5545), limited to the subset the app
/// supports: FREQ=DAILY or WEEKLY, INTERVAL, BYDAY (weekly only), WKST, and COUNT or UNTIL (a date). One canonical form
/// is written, with the parts in that order and the days in week order, so the same rule is always the same text.
/// Reading accepts the parts in any order and refuses anything else, naming what is wrong.
/// The app's own meaning applies (see <see cref="RecurrenceRule"/>), so this text is for the app's own storage, not
/// for handing to other calendars as it is. Two differences would need converting on export: the first date only
/// counts if the rule falls on it (iCalendar always counts DTSTART), and UNTIL is the app's inclusive last date,
/// whereas RFC 5545 wants UNTIL as a UTC date-time when the series starts at a time of day, as the app's do.
/// </summary>
public static class RecurrenceRuleText
{
    private static readonly LocalDatePattern UntilPattern = LocalDatePattern.CreateWithInvariantCulture("uuuuMMdd");

    private static readonly (string Code, IsoDayOfWeek Day)[] DayCodes =
    [
        ("MO", IsoDayOfWeek.Monday), ("TU", IsoDayOfWeek.Tuesday), ("WE", IsoDayOfWeek.Wednesday), ("TH", IsoDayOfWeek.Thursday),
        ("FR", IsoDayOfWeek.Friday), ("SA", IsoDayOfWeek.Saturday), ("SU", IsoDayOfWeek.Sunday),
    ];

    private const string Prefix = "RRULE:";

    private static readonly string[] KnownParts = ["FREQ", "INTERVAL", "BYDAY", "WKST", "COUNT", "UNTIL"];

    public static string Format(RecurrenceRule rule)
    {
        var parts = new List<string>
        {
            rule.Frequency == RepeatFrequency.Daily ? "FREQ=DAILY" : "FREQ=WEEKLY",
            $"INTERVAL={rule.Every.ToString(CultureInfo.InvariantCulture)}",
        };

        if (rule.Frequency == RepeatFrequency.Weekly)
        {
            parts.Add("BYDAY=" + string.Join(",", DayCodes.Where(code => rule.Days.Contains(code.Day)).Select(code => code.Code)));
            parts.Add("WKST=" + CodeFor(rule.WeekStart));
        }

        if (rule.Count is { } count)
        {
            parts.Add($"COUNT={count.ToString(CultureInfo.InvariantCulture)}");
        }

        if (rule.LastDate is { } lastDate)
        {
            parts.Add("UNTIL=" + UntilPattern.Format(lastDate));
        }

        return string.Join(";", parts);
    }

    /// <summary>
    /// Reads RRULE text written by <see cref="Format"/>, also accepting the parts in any order, any letter case, spaces
    /// around parts, values and day codes, and the "RRULE:" prefix used in .ics files. Throws FormatException naming
    /// the part that is wrong.
    /// </summary>
    public static RecurrenceRule Parse(string text)
    {
        text = text.Trim();
        if (text.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            text = text[Prefix.Length..];
        }

        var parts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = part.IndexOf('=');
            var name = (separator < 0 ? part : part[..separator]).Trim().ToUpperInvariant();
            var value = separator < 0 ? "" : part[(separator + 1)..].Trim().ToUpperInvariant();
            if (!KnownParts.Contains(name))
            {
                throw new FormatException($"The repeat rule has a part the app doesn't support: {part}.");
            }

            if (!parts.TryAdd(name, value))
            {
                throw new FormatException($"The repeat rule gives {name} more than once.");
            }
        }

        if (!parts.TryGetValue("FREQ", out var frequency))
        {
            throw new FormatException("The repeat rule has no FREQ (how often it repeats).");
        }

        var every = parts.TryGetValue("INTERVAL", out var interval) ? PositiveNumber(interval, "INTERVAL") : 1;
        // Read for daily rules too, so a week start that isn't a day is refused there as well (a daily rule doesn't use it).
        var weekStart = parts.TryGetValue("WKST", out var weekStartCode) ? DayFor(weekStartCode, "WKST") : IsoDayOfWeek.Monday;

        var rule = frequency switch
        {
            "DAILY" when parts.ContainsKey("BYDAY") => throw new FormatException("A daily repeat rule can't have BYDAY."),
            "DAILY" => RecurrenceRule.Daily(every),
            "WEEKLY" when !parts.TryGetValue("BYDAY", out _) => throw new FormatException("A weekly repeat rule needs BYDAY (which days)."),
            "WEEKLY" => RecurrenceRule.Weekly(
                parts["BYDAY"].Split(',', StringSplitOptions.TrimEntries).Select(code => DayFor(code, "BYDAY")),
                every,
                weekStart),
            _ => throw new FormatException($"FREQ must be DAILY or WEEKLY, not \"{frequency}\": the app only repeats daily or weekly."),
        };

        if (parts.ContainsKey("COUNT") && parts.ContainsKey("UNTIL"))
        {
            throw new FormatException("A repeat rule can end after a COUNT or on a date (UNTIL), not both.");
        }

        if (parts.TryGetValue("COUNT", out var countText))
        {
            return rule.EndingAfter(PositiveNumber(countText, "COUNT"));
        }

        if (parts.TryGetValue("UNTIL", out var untilText))
        {
            var until = UntilPattern.Parse(untilText);
            return until.Success
                ? rule.EndingOn(until.Value)
                : throw new FormatException($"UNTIL must be a date written as YYYYMMDD, not {untilText}.");
        }

        return rule;
    }

    private static int PositiveNumber(string text, string part)
    {
        var parsed = int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number);
        if (parsed && number >= 1)
        {
            return number;
        }

        // Only digits, yet not a number: more digits than an int can hold.
        throw new FormatException(!parsed && text.Length > 0 && text.All(char.IsAsciiDigit)
            ? $"{part} is too big: {text}."
            : $"{part} must be a whole number of 1 or more, not \"{text}\".");
    }

    private static IsoDayOfWeek DayFor(string code, string part) =>
        DayCodes.FirstOrDefault(day => day.Code == code) is { Code: not null } found
            ? found.Day
            : throw new FormatException($"{part} must be days of the week written as MO, TU, WE, TH, FR, SA or SU, not \"{code}\".");

    private static string CodeFor(IsoDayOfWeek day) => DayCodes.Single(code => code.Day == day).Code;
}
