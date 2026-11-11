using FitnessTracker.Domain.Workouts;
using FitnessTracker.Web.Data;
using FitnessTracker.Web.Models.Goals;

namespace FitnessTracker.Web.Tests.Goals;

// FIT-5, Q24, Q37: a goal's week in words, with the number its bar shows, so the bar is never the only signal (design
// rule 1). An exercise goal counts what's done (Q20: its sessions have statuses); one without statuses, what's planned.
public class GoalDisplayTests
{
    private static readonly WeeklyGoal Gym = new() { Title = "Gym", Category = ActivityCategory.Gym, SessionsPerWeek = 3 };

    [Fact]
    public void AnExerciseGoal_CountsWhatsDone_AndSaysTheRest()
    {
        var week = GoalDisplay.WeekOf(Gym, new GoalProgress(3, Done: 1, Skipped: 1, NotLogged: 1, ToCome: 1, Planned: 0));

        Assert.Equal(("Gym", "1 of 3 done", "1 skipped · 1 not logged · 1 to come"), (week.Label, week.Headline, week.Details));
        Assert.Equal((1, 3), (week.Value, week.Max));
    }

    [Fact]
    public void NothingPlannedYet_IsSaid()
    {
        var week = GoalDisplay.WeekOf(Gym, new GoalProgress(3, 0, 0, 0, 0, 0));

        Assert.Equal(("0 of 3 done", "3 not planned yet"), (week.Headline, week.Details));
    }

    // Encouraging, never a failure (design.md: progress that encourages rather than punishes).
    [Fact]
    public void AllDone_IsAGoalMet()
    {
        var week = GoalDisplay.WeekOf(Gym, new GoalProgress(3, Done: 3, 0, 0, 0, 0));

        Assert.Equal(("3 of 3 done: goal met", null), (week.Headline, week.Details));
        Assert.Equal((3, 3), (week.Value, week.Max));
    }

    // More than the goal asks for: said as it is, and the bar is full.
    [Fact]
    public void MoreThanTheGoal_FillsTheBar()
    {
        var week = GoalDisplay.WeekOf(Gym, new GoalProgress(2, Done: 3, 0, 0, 0, 0));

        Assert.Equal("3 of 2 done: goal met", week.Headline);
        Assert.Equal((2, 2), (week.Value, week.Max));
    }

    // Q20: meal prep has no statuses, so its sessions are counted as planned.
    [Fact]
    public void AGoalWithoutStatuses_CountsWhatsPlanned()
    {
        var prep = new WeeklyGoal { Title = "Sunday prep", Category = ActivityCategory.MealPrep, SessionsPerWeek = 2 };

        var week = GoalDisplay.WeekOf(prep, new GoalProgress(2, 0, 0, 0, 0, Planned: 1));

        Assert.Equal(("1 of 2 planned", "1 not planned yet"), (week.Headline, week.Details));
        Assert.Equal((1, 2), (week.Value, week.Max));
    }

    // Q39: "Counts towards" names each goal with its category beside it, unless its name already is the category's
    // ("Gym", not "Gym (Gym)"), ignoring capitals. Found by the browser check of FIT-5.
    [Theory]
    [InlineData("Gym", ActivityCategory.Gym, "Gym")]
    [InlineData("gym", ActivityCategory.Gym, "gym")]
    [InlineData("Meal prep", ActivityCategory.MealPrep, "Meal prep")]
    [InlineData("Gym three times", ActivityCategory.Gym, "Gym three times (Gym)")]
    [InlineData("Run", ActivityCategory.Gym, "Run (Gym)")]
    public void AChoice_HasItsCategoryBeside_UnlessItsNameIsTheCategorys(string title, ActivityCategory category, string expected) =>
        Assert.Equal(expected, GoalDisplay.Choice(new WeeklyGoal { Title = title, Category = category, SessionsPerWeek = 1 }));

    // Where the goal's name is already shown (the Goals page), the words say "This week" instead; the bar is still named
    // after the goal for a screen reader.
    [Fact]
    public void ALabel_TakesTheTitlesPlace_ButTheBarIsStillNamedAfterTheGoal()
    {
        var week = GoalDisplay.WeekOf(Gym, new GoalProgress(3, 0, 0, 0, 0, 0), label: "This week");

        Assert.Equal(("This week", "Gym: 0 of 3 done"), (week.Label, week.BarName));
    }
}
