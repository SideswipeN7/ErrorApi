namespace ErrorApi.Generator.Helpers;

/// <summary>
/// The emit-time twin of <c>ErrorApi.EndpointGroup.Normalize</c>: the generated lookup switches on
/// normalized group names, so the case labels must be normalized with exactly the same rules the
/// runtime applies to the incoming name. Change one, change both — a test pins them together.
/// </summary>
internal static class GroupNormalizer
{
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

        while (value.Length > 2 && value.EndsWith(".0", System.StringComparison.Ordinal) && IsVersionNumber(value))
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
