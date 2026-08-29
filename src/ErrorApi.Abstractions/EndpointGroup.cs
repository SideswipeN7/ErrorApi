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
    /// <c>"1.0"</c> are the same group. Kept in step with its emit-time twin,
    /// <c>GroupNormalizer.Normalize</c> in the generator — change one, change both.
    /// </summary>
    public static string? Normalize(string? group)
    {
        if (group is null)
        {
            return null;
        }

        var value = group.Trim();
        if (value.Length == 0)
        {
            return null;
        }

        value = value.ToLowerInvariant();

        if (value.Length > 1 && value[0] == 'v' && value[1] >= '0' && value[1] <= '9')
        {
            value = value.Substring(1);
        }

        while (value.Length > 2 && value.EndsWith(".0", StringComparison.Ordinal) && IsVersionNumber(value))
        {
            value = value.Substring(0, value.Length - 2);
        }

        return value;
    }

    private static bool IsVersionNumber(string value)
    {
        foreach (var c in value)
        {
            if ((c < '0' || c > '9') && c != '.')
            {
                return false;
            }
        }

        return true;
    }
}
