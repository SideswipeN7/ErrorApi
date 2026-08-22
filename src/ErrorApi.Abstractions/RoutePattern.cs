using System.Text;

namespace ErrorApi;

/// <summary>
/// Brings route templates written at the call site (<c>MapGet("/orders/{id:guid}")</c>) and templates
/// reported at runtime (<c>ApiDescription.RelativePath</c>) into one comparable form.
/// </summary>
/// <remarks>
/// The generator applies the identical transform when it emits the endpoint table, so lookups are
/// plain ordinal string comparisons. Keep both copies in step — see <c>RouteNormalizationTests</c>.
/// </remarks>
public static class RoutePattern
{
    /// <summary>
    /// Normalizes a route template: leading slash, no trailing slash, lower-cased literal segments,
    /// and route parameters reduced to <c>{name}</c> with constraints, defaults and modifiers stripped.
    /// </summary>
    public static string Normalize(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return "/";
        }

        var builder = new StringBuilder(pattern!.Length + 1);
        builder.Append('/');

        var index = 0;
        var length = pattern.Length;

        while (index < length && pattern[index] == '/')
        {
            index++;
        }

        while (index < length)
        {
            var c = pattern[index];
            if (c == '{')
            {
                var depth = 0;
                var start = ++index;
                var nameEnd = -1;

                while (index < length)
                {
                    var inner = pattern[index];
                    if (inner == '{')
                    {
                        depth++;
                    }
                    else if (inner == '}')
                    {
                        if (depth == 0)
                        {
                            break;
                        }

                        depth--;
                    }
                    else if (depth == 0 && nameEnd < 0 && (inner == ':' || inner == '=' || inner == '?'))
                    {
                        nameEnd = index;
                    }

                    index++;
                }

                if (nameEnd < 0)
                {
                    nameEnd = index;
                }

                var name = pattern.Substring(start, nameEnd - start).TrimStart('*');
                builder.Append('{').Append(name).Append('}');
                index++; // consume '}'
                continue;
            }

            builder.Append(char.ToLowerInvariant(c));
            index++;
        }

        while (builder.Length > 1 && builder[builder.Length - 1] == '/')
        {
            builder.Length--;
        }

        return builder.ToString();
    }

    /// <summary>Joins a route group prefix with a nested pattern before normalization.</summary>
    public static string Combine(string? prefix, string? pattern)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return Normalize(pattern);
        }

        if (string.IsNullOrWhiteSpace(pattern))
        {
            return Normalize(prefix);
        }

        return Normalize(prefix!.TrimEnd('/') + "/" + pattern!.TrimStart('/'));
    }
}
