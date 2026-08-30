namespace ErrorApi;

/// <summary>
/// Brings route templates written at the call site (<c>MapGet("/orders/{id:guid}")</c>) and templates
/// reported at runtime (<c>ApiDescription.RelativePath</c>) into one comparable form.
/// </summary>
/// <remarks>
/// The generator applies the identical transform when it emits the endpoint table, so lookups are
/// plain ordinal string comparisons. Both sides compile the same linked source —
/// <c>src/Shared/SharedNormalization.cs</c> — so they cannot drift.
/// </remarks>
public static class RoutePattern
{
    /// <summary>
    /// Normalizes a route template: leading slash, no trailing slash, lower-cased literal segments,
    /// and route parameters reduced to <c>{name}</c> with constraints, defaults and modifiers stripped.
    /// </summary>
    public static string Normalize(string? pattern) => Shared.SharedNormalization.NormalizeRoute(pattern);

    /// <summary>Joins a route group prefix with a nested pattern before normalization.</summary>
    public static string Combine(string? prefix, string? pattern) => Shared.SharedNormalization.CombineRoute(prefix, pattern);
}
