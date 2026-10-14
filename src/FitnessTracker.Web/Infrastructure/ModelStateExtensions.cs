using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace FitnessTracker.Web.Infrastructure;

public static class ModelStateExtensions
{
    /// <summary>"true" when the field has a validation error, otherwise null (so Razor leaves the attribute out).</summary>
    public static string? AriaInvalid(this ModelStateDictionary modelState, string key) =>
        modelState.TryGetValue(key, out var entry) && entry.Errors.Count > 0 ? "true" : null;

    /// <summary>
    /// Every problem, for the error summary: problems with the whole form first, then each field's in the order the
    /// form's model declares its fields. Field is empty when there is no field on the form to link to (a problem
    /// with the whole form, or with something that isn't a form field, such as a value in the address).
    /// </summary>
    public static IReadOnlyList<(string Field, string Message)> ProblemsInFieldOrder(
        this ModelStateDictionary modelState, ModelMetadata model)
    {
        var fieldOrder = model.Properties.Select(property => property.PropertyName).ToList();
        return modelState
            .Where(entry => entry.Value is { Errors.Count: > 0 })
            .Select(entry => (Position: fieldOrder.IndexOf(entry.Key), Entry: entry.Value!))
            .OrderBy(problem => problem.Position)
            .SelectMany(problem => problem.Entry.Errors.Select(error =>
                (Field: problem.Position >= 0 ? fieldOrder[problem.Position]! : "", error.ErrorMessage)))
            .ToList();
    }
}
