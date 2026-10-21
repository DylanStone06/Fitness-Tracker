using FitnessTracker.Domain.Time;
using NodaTime;

namespace FitnessTracker.Domain.Tests.Time;

// ACC-3: times are shown the way the user chose in Preferences: 24-hour ("18:30") or 12-hour ("6:30pm", as the
// Preferences page itself describes it).
public class ClockTextTests
{
    [Theory]
    [InlineData(7, 0, "07:00", "7:00am")]
    [InlineData(18, 30, "18:30", "6:30pm")]
    [InlineData(0, 5, "00:05", "12:05am")]
    [InlineData(12, 0, "12:00", "12:00pm")]
    [InlineData(23, 59, "23:59", "11:59pm")]
    public void ATime_IsWrittenTheChosenWay(int hour, int minute, string twentyFourHour, string twelveHour)
    {
        var time = new LocalTime(hour, minute);

        Assert.Equal(twentyFourHour, ClockText.Format(time, ClockFormat.TwentyFourHour));
        Assert.Equal(twelveHour, ClockText.Format(time, ClockFormat.TwelveHour));
    }

    // §8, T4, T5: a clock time as it really happens. 01:30 on the night the clocks go forward is 02:30; on the night they
    // go back it is the first 01:30, so it says which.
    [Theory]
    [InlineData(2027, 3, 28, "02:30", "2:30am")]
    [InlineData(2026, 10, 25, "01:30 BST", "1:30am BST")]
    [InlineData(2026, 11, 2, "01:30", "1:30am")]
    public void AClockTime_IsWrittenAsItReallyHappens(int year, int month, int day, string twentyFourHour, string twelveHour)
    {
        var london = DateTimeZoneProviders.Tzdb["Europe/London"];
        var clockTime = new LocalDateTime(year, month, day, 1, 30);

        Assert.Equal(twentyFourHour, ClockText.At(clockTime, london, ClockFormat.TwentyFourHour));
        Assert.Equal(twelveHour, ClockText.At(clockTime, london, ClockFormat.TwelveHour));
    }
}
