using FitnessTracker.Web.Data;
using FitnessTracker.Web.Models.Activities;

namespace FitnessTracker.Web.Tests.Activities;

// CAL-2: each category has one colour, the same on every page (docs/design.md): blue for work and commuting, green for
// exercise, orange for meal prep, purple for social plans and appointments, grey for anything else. The colour is
// never the only signal: the category's name is shown with it.
public sealed class ActivityDisplayTests
{
    [Theory]
    [InlineData(ActivityCategory.Work, "cat-work")]
    [InlineData(ActivityCategory.Commute, "cat-work")]
    [InlineData(ActivityCategory.Gym, "cat-exercise")]
    [InlineData(ActivityCategory.Run, "cat-exercise")]
    [InlineData(ActivityCategory.OtherExercise, "cat-exercise")]
    [InlineData(ActivityCategory.MealPrep, "cat-meal")]
    [InlineData(ActivityCategory.Social, "cat-social")]
    [InlineData(ActivityCategory.Appointment, "cat-social")]
    [InlineData(ActivityCategory.Other, "cat-other")]
    public void EachCategory_HasItsColour(ActivityCategory category, string expected)
    {
        Assert.Equal(expected, ActivityDisplay.CategoryStyle(category));
    }

    // A category added later fails here until its colour is chosen, rather than quietly turning grey.
    [Fact]
    public void EveryCategory_HasAColourChosenForIt()
    {
        foreach (var category in Enum.GetValues<ActivityCategory>())
        {
            Assert.True(ActivityDisplay.CategoryStyles.ContainsKey(category), $"No colour chosen for {category}.");
        }
    }

    // A value no category has (edited into the database by hand) is grey, so the page still opens.
    [Fact]
    public void AnUnknownCategory_IsGrey()
    {
        Assert.Equal("cat-other", ActivityDisplay.CategoryStyle((ActivityCategory)99));
    }
}
