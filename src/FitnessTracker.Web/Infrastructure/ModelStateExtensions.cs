using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace FitnessTracker.Web.Infrastructure;

public static class ModelStateExtensions
{
    /// <summary>"true" when the field has a validation error, otherwise null (so Razor leaves the attribute out).</summary>
    public static string? AriaInvalid(this ModelStateDictionary modelState, string key) =>
        modelState.TryGetValue(key, out var entry) && entry.Errors.Count > 0 ? "true" : null;
}
