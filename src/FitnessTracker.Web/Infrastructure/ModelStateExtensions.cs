using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace FitnessTracker.Web.Infrastructure;

public static class ModelStateExtensions
{
    /// <summary>"true" when the field has a validation error, otherwise null (so Razor leaves the attribute out).</summary>
    public static string? AriaInvalid(this ModelStateDictionary modelState, string key) =>
        modelState.TryGetValue(key, out var entry) && entry.Errors.Count > 0 ? "true" : null;

    /// <summary>
    /// Every problem, for the error summary: problems with the whole form first, then each field's in the order the
    /// form's model declares its fields. A problem inside a list on the form ("OpeningDays[0].Opens") keeps its full
    /// name, so it links to that entry, and is placed where the list is. Field is empty when there is no field on the
    /// form to link to (a problem with the whole form, or with something that isn't a form field, such as a value in
    /// the address).
    /// </summary>
    public static IReadOnlyList<(string Field, string Message)> ProblemsInFieldOrder(
        this ModelStateDictionary modelState, ModelMetadata model)
    {
        var fieldOrder = model.Properties.Select(property => property.PropertyName!).ToList();
        return modelState
            .Where(entry => entry.Value is { Errors.Count: > 0 })
            .Select(entry => (Position: PositionOf(entry.Key, fieldOrder), entry.Key, Entry: entry.Value!))
            .OrderBy(problem => problem.Position)
            .SelectMany(problem => problem.Entry.Errors.Select(error =>
                (Field: problem.Position >= 0 ? problem.Key : "", error.ErrorMessage)))
            .ToList();
    }

    // The position of the form field a model-state key belongs to: the field itself, or an entry inside it.
    private static int PositionOf(string key, List<string> fieldOrder) =>
        fieldOrder.FindIndex(field =>
            key == field || key.StartsWith(field + "[", StringComparison.Ordinal) || key.StartsWith(field + ".", StringComparison.Ordinal));
}
