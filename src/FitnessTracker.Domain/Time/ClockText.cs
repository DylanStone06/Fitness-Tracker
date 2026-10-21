using System.Globalization;
using NodaTime;
using NodaTime.Text;

namespace FitnessTracker.Domain.Time;

/// <summary>ACC-3: how the user wants times shown.</summary>
public enum ClockFormat
{
    TwentyFourHour,
    TwelveHour,
}

/// <summary>
/// Times as the explanations and pages write them: "18:30", or "6:30pm" with the 12-hour clock (the Preferences page's
/// own example). Form boxes are different: a browser's time box always takes "18:30".
/// </summary>
public static class ClockText
{
    private static readonly LocalTimePattern TwentyFourHour = LocalTimePattern.CreateWithInvariantCulture("HH:mm");

    public static string Format(LocalTime time, ClockFormat clock)
    {
        if (clock == ClockFormat.TwentyFourHour)
        {
            return TwentyFourHour.Format(time);
        }

        var hour = time.Hour % 12 == 0 ? 12 : time.Hour % 12;
        return string.Create(CultureInfo.InvariantCulture, $"{hour}:{time.Minute:00}{(time.Hour < 12 ? "am" : "pm")}");
    }
}
