namespace ErrorApi.Generator.Helpers;

/// <summary>
/// Compile-time face of <c>ErrorApi.RoutePattern</c>. The generator cannot reference the runtime
/// assembly, so both compile the same linked source — <c>src/Shared/SharedNormalization.cs</c> —
/// and cannot drift; <c>RouteNormalizationTests</c> stands witness.
/// </summary>
internal static class RouteNormalizer
{
    public static string Normalize(string? pattern) => Shared.SharedNormalization.NormalizeRoute(pattern);

    public static string Combine(string? prefix, string? pattern) => Shared.SharedNormalization.CombineRoute(prefix, pattern);
}
