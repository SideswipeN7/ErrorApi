namespace ErrorApi;

/// <summary>
/// How API description group names are matched when endpoint errors are resolved. Group names name the
/// same version in several spellings — <c>WithGroupName("v1")</c>, Asp.Versioning's default
/// <c>"1.0"</c>, its conventional <c>'v'VVV</c> format's <c>"v1"</c> — so the generated lookup matches
/// them through this normalization instead of by exact string.
/// </summary>
public static class EndpointGroup
{
    /// <summary>
    /// Normalizes a group name for matching: trimmed, lower-cased, a leading <c>v</c> before a digit
    /// dropped, and a trailing <c>.0</c> of a numeric version dropped — so <c>"v1"</c>, <c>"V1"</c> and
    /// <c>"1.0"</c> are the same group. The generator's case labels come from the same linked source
    /// (<c>src/Shared/SharedNormalization.cs</c>), so the two sides cannot drift.
    /// </summary>
    public static string? Normalize(string? group) => Shared.SharedNormalization.NormalizeGroup(group);
}
