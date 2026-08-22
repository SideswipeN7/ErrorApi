using System.Text;

namespace ErrorApi.Generator.Helpers;

/// <summary>
/// Compile-time twin of <c>ErrorApi.RoutePattern</c>. The generator cannot reference the runtime
/// assembly, so the transform is duplicated here; <c>RouteNormalizationTests</c> pins the two together.
/// </summary>
internal static class RouteNormalizer
{
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
                index++;
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
