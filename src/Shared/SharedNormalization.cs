using System;
using System.Text;

namespace ErrorApi.Shared;

/// <summary>
/// The one implementation of every transform that must agree between the generator (emit time) and
/// the runtime: route normalization, group-name normalization, and the assembly-name-to-namespace
/// sanitizer. Compiled into <c>ErrorApi.Abstractions</c>, <c>ErrorApi.AspNetCore</c> and
/// <c>ErrorApi.Generator</c> as a linked source file, because the generator cannot reference the
/// runtime assemblies — one file instead of three pairs of hand-synchronized twins.
/// </summary>
internal static class SharedNormalization
{
    /// <summary>
    /// Normalizes a route template: leading slash, no trailing slash, lower-cased literal segments,
    /// and route parameters reduced to <c>{name}</c> with constraints, defaults and modifiers stripped.
    /// </summary>
    public static string NormalizeRoute(string? pattern)
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
    public static string CombineRoute(string? prefix, string? pattern)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return NormalizeRoute(pattern);
        }

        if (string.IsNullOrWhiteSpace(pattern))
        {
            return NormalizeRoute(prefix);
        }

        return NormalizeRoute(prefix!.TrimEnd('/') + "/" + pattern!.TrimStart('/'));
    }

    /// <summary>
    /// Normalizes an API description group name for matching: trimmed, lower-cased, a leading
    /// <c>v</c> before a digit dropped, and a trailing <c>.0</c> of a numeric version dropped — so
    /// <c>"v1"</c>, <c>"V1"</c> and <c>"1.0"</c> are the same group.
    /// </summary>
    public static string? NormalizeGroup(string? group)
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

    /// <summary>
    /// The namespace derived from an assembly name — where the generated <c>ErrorApiModel</c> accessor
    /// lives, and where <c>IncludeFromAssemblies</c> finds it again at startup.
    /// </summary>
    public static string SanitizeNamespace(string assemblyName)
    {
        if (string.IsNullOrEmpty(assemblyName))
        {
            return "ErrorApiAssembly";
        }

        var builder = new StringBuilder(assemblyName.Length);
        var startOfSegment = true;

        foreach (var c in assemblyName)
        {
            if (c == '.')
            {
                builder.Append('.');
                startOfSegment = true;
                continue;
            }

            var valid = char.IsLetterOrDigit(c) || c == '_' ? c : '_';
            if (startOfSegment && char.IsDigit(valid))
            {
                builder.Append('_');
            }

            builder.Append(valid);
            startOfSegment = false;
        }

        return builder.ToString();
    }
}
