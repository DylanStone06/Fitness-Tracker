using FitnessTracker.Web.Infrastructure;
using FitnessTracker.Web.Models.Account;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace FitnessTracker.Web.Tests.Infrastructure;

public sealed class ModelStateExtensionsTests
{
    // Screen readers announce a field as invalid only when aria-invalid="true" is present; Razor drops
    // the attribute entirely when the value is null.
    [Fact]
    public void AriaInvalid_IsTrue_OnlyForAFieldWithAnError()
    {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("Email", "Enter your email address.");
        modelState.SetModelValue("Password", "a long test passphrase", "a long test passphrase");

        Assert.Equal("true", modelState.AriaInvalid("Email"));
        Assert.Null(modelState.AriaInvalid("Password"));
        Assert.Null(modelState.AriaInvalid("NotSubmitted"));
    }

    // A problem with one entry of a list on the form (Monday's opening time) links to that entry's own field, in the
    // place of the list among the form's fields.
    [Fact]
    public void ProblemsInFieldOrder_LinksAProblemInsideAListToThatEntry()
    {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("OpeningDays[0].Opens", "Enter the opening time.");
        modelState.AddModelError("Name", "Enter a name for the location.");
        var form = new EmptyModelMetadataProvider().GetMetadataForType(typeof(FitnessTracker.Web.Models.Locations.LocationFormViewModel));

        (string, string)[] expected = [("Name", "Enter a name for the location."), ("OpeningDays[0].Opens", "Enter the opening time.")];
        Assert.Equal(expected, modelState.ProblemsInFieldOrder(form));
    }

    // A problem with something that isn't a field on the form (here a value from the address) has nothing to link
    // to, so it is listed like a problem with the whole form: no field, before the fields' own problems.
    [Fact]
    public void ProblemsInFieldOrder_GivesNoFieldForAProblemThatIsNotAFormField()
    {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("Password", "Enter a password.");
        modelState.AddModelError("returnUrl", "The return address isn't valid.");
        var form = new EmptyModelMetadataProvider().GetMetadataForType(typeof(RegisterViewModel));

        (string, string)[] expected = [("", "The return address isn't valid."), ("Password", "Enter a password.")];
        Assert.Equal(expected, modelState.ProblemsInFieldOrder(form));
    }
}
