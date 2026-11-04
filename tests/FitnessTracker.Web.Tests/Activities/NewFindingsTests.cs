using FitnessTracker.Web.Services;

namespace FitnessTracker.Web.Tests.Activities;

// Q8, SCH-1: after a change that moves activities without their own form (a timezone change, a routine chosen for some
// dates), what it broke is what the check finds after it that it didn't before. A rule broken on several dates is one
// sentence naming the first and counting the rest, so the counts are compared too (scheduling review, Phase 6b).
public class NewFindingsTests
{
    private const string OnTwoDates = "Hard ride: Wed 4 Nov 2026 is one of your recovery days, and this is a demanding session. The same happens on 1 more date up to Tue 19 Oct 2027.";
    private const string OnThreeDates = "Hard ride: Wed 4 Nov 2026 is one of your recovery days, and this is a demanding session. The same happens on 2 more dates up to Tue 19 Oct 2027.";
    private const string OnOneDate = "Hard ride: Wed 4 Nov 2026 is one of your recovery days, and this is a demanding session.";

    [Fact]
    public void AFindingThatWasntThereBefore_IsNew()
    {
        Assert.Equal([OnOneDate], ActivityChecker.NewFindings([OnOneDate], []));
    }

    [Fact]
    public void AFindingThatWasThereBefore_IsNotNew()
    {
        Assert.Empty(ActivityChecker.NewFindings([OnTwoDates], [OnTwoDates]));
    }

    // The change fixed one of the dates: nothing new is broken.
    [Fact]
    public void ARuleBrokenOnFewerDates_IsNotNew()
    {
        Assert.Empty(ActivityChecker.NewFindings([OnOneDate], [OnTwoDates]));
    }

    // The change broke it on another date too: that is new, said with the dates as they are now.
    [Fact]
    public void ARuleBrokenOnMoreDates_IsNew()
    {
        Assert.Equal([OnThreeDates], ActivityChecker.NewFindings([OnThreeDates], [OnTwoDates]));
        Assert.Equal([OnTwoDates], ActivityChecker.NewFindings([OnTwoDates], [OnOneDate]));
    }
}
