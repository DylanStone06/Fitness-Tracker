using System.Text;
using System.Text.Json;
using NodaTime;
using NodaTime.Text;

namespace FitnessTracker.Domain.Recurrence;

/// <summary>
/// CAL-4: a series' one-off changes stored as text with the activity, like its rule (<see cref="RecurrenceRuleText"/>).
/// A JSON list in date order, so the same changes are always the same text:
/// <c>[{"date":"2026-11-04","cancelled":true},{"date":"2026-11-11","start":"2026-11-11T19:00","minutes":90}]</c>.
/// "date" is the date the rule puts the occurrence on, which identifies it even after a move; "start" is its new clock
/// time in the series' timezone, and "minutes" its new elapsed length (§8). Reading is strict: anything else is refused
/// with a reason, so a change is never half-applied.
/// </summary>
public static class OccurrenceChangesText
{
    private static readonly LocalDatePattern DatePattern = LocalDatePattern.Iso;

    private static readonly LocalDateTimePattern StartPattern = LocalDateTimePattern.CreateWithInvariantCulture("uuuu'-'MM'-'dd'T'HH':'mm");

    private static readonly LocalDatePattern Named = LocalDatePattern.CreateWithInvariantCulture("ddd d MMM uuuu");

    private static readonly string[] CancelledFields = ["date", "cancelled"];

    private static readonly string[] MovedFields = ["date", "start", "minutes"];

    /// <summary>The changes as text, or null when there are none. Times and lengths must be whole minutes.</summary>
    public static string? Format(IReadOnlyDictionary<LocalDate, OccurrenceChange> changes)
    {
        if (changes.Count == 0)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartArray();
            foreach (var (date, change) in changes.OrderBy(change => change.Key))
            {
                json.WriteStartObject();
                json.WriteString("date", DatePattern.Format(date));
                switch (change)
                {
                    case OccurrenceChange.Cancelled:
                        json.WriteBoolean("cancelled", true);
                        break;
                    case OccurrenceChange.Moved move:
                        if (move.Length.BclCompatibleTicks % NodaConstants.TicksPerMinute != 0 || move.Start.Second != 0 || move.Start.TickOfSecond != 0)
                        {
                            throw new ArgumentException($"The change on {Named.Format(date)} isn't in whole minutes.", nameof(changes));
                        }

                        json.WriteString("start", StartPattern.Format(move.Start));
                        json.WriteNumber("minutes", (long)move.Length.TotalMinutes);
                        break;
                }

                json.WriteEndObject();
            }

            json.WriteEndArray();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// <paramref name="series"/> with the changes in <paramref name="text"/> (none for null or empty). Throws
    /// FormatException, saying what is wrong, for text the app didn't write, or a change on a date the series doesn't
    /// fall on.
    /// </summary>
    public static Series Apply(Series series, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return series;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            throw new FormatException("The one-off changes can't be read: they aren't the text the app writes.");
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new FormatException("The one-off changes should be a list.");
            }

            var seen = new HashSet<LocalDate>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                var date = item.ValueKind == JsonValueKind.Object && Text(item, "date") is { } dateText && DatePattern.Parse(dateText) is { Success: true } parsed
                    ? parsed.Value
                    : throw new FormatException("A one-off change has no date that can be read.");
                if (!seen.Add(date))
                {
                    throw new FormatException($"{Named.Format(date)} is changed twice.");
                }

                var cancelled = item.TryGetProperty("cancelled", out var flag) && flag.ValueKind == JsonValueKind.True;
                var moved = item.TryGetProperty("start", out _);
                if (cancelled == moved)
                {
                    throw new FormatException(cancelled
                        ? $"{Named.Format(date)} is both cancelled and moved."
                        : $"{Named.Format(date)} is neither cancelled nor moved.");
                }

                // Nothing else is quietly ignored: a field the app doesn't write means it wasn't the app that wrote it.
                var fields = cancelled ? CancelledFields : MovedFields;
                if (item.EnumerateObject().Select(property => property.Name).FirstOrDefault(name => !fields.Contains(name)) is { } extra)
                {
                    throw new FormatException($"{Named.Format(date)} has \"{extra}\", which a {(cancelled ? "cancelled" : "moved")} occurrence doesn't.");
                }

                series = cancelled
                    ? Change(date, () => series.CancelOccurrence(date))
                    : Change(date, () => series.MoveOccurrence(date, StartOf(item, date), LengthOf(item, date)));
            }
        }

        return series;
    }

    // A change on a date the rule doesn't fall on: the activity was edited outside the app.
    private static Series Change(LocalDate date, Func<Series> change)
    {
        try
        {
            return change();
        }
        catch (ArgumentException)
        {
            throw new FormatException($"{Named.Format(date)} is changed, but the activity doesn't happen that day.");
        }
    }

    private static LocalDateTime StartOf(JsonElement item, LocalDate date) =>
        Text(item, "start") is { } start && StartPattern.Parse(start) is { Success: true } parsed
            ? parsed.Value
            : throw new FormatException($"The new start for {Named.Format(date)} can't be read.");

    private static Duration LengthOf(JsonElement item, LocalDate date) =>
        item.TryGetProperty("minutes", out var minutes) && minutes.ValueKind == JsonValueKind.Number && minutes.TryGetInt32(out var value) && value > 0
            ? Duration.FromMinutes(value)
            : throw new FormatException($"The new length for {Named.Format(date)} should be a number of minutes, more than 0.");

    private static string? Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
