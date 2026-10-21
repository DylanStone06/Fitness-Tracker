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
}
