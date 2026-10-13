using FitnessTracker.Web.Infrastructure;
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
}
