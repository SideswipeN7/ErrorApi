using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ErrorApi.Generator.Helpers;

/// <summary>
/// Works out the values a catalog entry did not spell out. The wire code is usually written somewhere
/// already — in the factory call the member returns, or in the name of the declaration itself — and
/// repeating it in the attribute is the kind of duplication this generator exists to remove.
/// </summary>
internal static class NameInference
{
    public const string ErrorCatalogAttributeName = "ErrorApi.ErrorCatalogAttribute";

    /// <summary>Parameter names a library uses for the wire code of an error.</summary>
    private static readonly string[] CodeParameterNames = ["code", "errorCode"];

    /// <summary>
    /// Reads the wire code out of the member's own implementation: a string literal passed to a
    /// parameter called <c>code</c>, which is how ErrorOr and most factory-style error APIs spell it.
    /// Returns <see langword="null"/> when the declaration has no body to read, as on a partial member.
    /// </summary>
    public static string? CodeFromBody(SyntaxNode declaration, SemanticModel model)
    {
        foreach (var node in declaration.DescendantNodes())
        {
            var arguments = node switch
            {
                InvocationExpressionSyntax invocation => invocation.ArgumentList,
                BaseObjectCreationExpressionSyntax creation => creation.ArgumentList,
                _ => null,
            };

            if (arguments is null || model.GetSymbolInfo(node).Symbol is not IMethodSymbol method)
            {
                continue;
            }

            if (MatchStringArgument(arguments, method, CodeParameterNames, model) is { } code)
            {
                return code;
            }
        }

        return null;
    }

    private static string? MatchStringArgument(
        BaseArgumentListSyntax arguments, IMethodSymbol method, string[] parameterNames, SemanticModel model)
    {
        for (var i = 0; i < arguments.Arguments.Count; i++)
        {
            var argument = arguments.Arguments[i];

            var parameter = argument.NameColon is { } named
                ? method.Parameters.FirstOrDefault(p => p.Name == named.Name.Identifier.ValueText)
                : i < method.Parameters.Length
                    ? method.Parameters[i]
                    : null;

            if (parameter is null
                || parameter.Type.SpecialType != SpecialType.System_String
                || !parameterNames.Contains(parameter.Name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (model.GetConstantValue(argument.Expression).Value is string { Length: > 0 } value)
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves the wire code of an <c>[Error]</c>-annotated symbol: the explicit argument first, then a
    /// <c>code:</c> literal in the symbol's own body, then its name.
    /// </summary>
    /// <remarks>
    /// This mirrors the order <c>CatalogParser</c> applies, and exists so the reachability walk agrees
    /// with the catalog it is walking towards. A symbol from a referenced assembly has no body to read,
    /// so a catalog meant to be consumed from elsewhere should not lean on body inference.
    /// </remarks>
    public static string ResolveCode(
        ISymbol symbol, AttributeData attribute, Compilation compilation, Func<SyntaxTree, SemanticModel> semanticModel)
    {
        if (attribute.ConstructorArguments.Length == 2 && attribute.ConstructorArguments[0].Value is string declared)
        {
            return declared;
        }

        foreach (var reference in symbol.DeclaringSyntaxReferences)
        {
            var node = reference.GetSyntax();
            if (compilation.ContainsSyntaxTree(node.SyntaxTree) && CodeFromBody(node, semanticModel(node.SyntaxTree)) is { } fromBody)
            {
                return fromBody;
            }
        }

        return CodeFromName(symbol, symbol is INamedTypeSymbol);
    }

    /// <summary>
    /// Builds a wire code from the declaration's own name. A member takes its catalog's prefix; an
    /// annotated type takes a prefix only when one is declared for it, since its name already reads as
    /// an identity.
    /// </summary>
    public static string CodeFromName(ISymbol symbol, bool isErrorType)
    {
        var prefix = Prefix(symbol, isErrorType);
        return prefix.Length == 0 ? symbol.Name : prefix + "." + symbol.Name;
    }

    private static string Prefix(ISymbol symbol, bool isErrorType)
    {
        for (var type = symbol.ContainingType; type is not null; type = type.ContainingType)
        {
            if (DeclaredPrefix(type) is { } declared)
            {
                return declared;
            }
        }

        if (symbol.ContainingAssembly is { } assembly && DeclaredPrefix(assembly) is { } assemblyPrefix)
        {
            return assemblyPrefix;
        }

        // A type names the failure on its own; a member needs its catalog to say which feature it belongs to.
        return isErrorType ? string.Empty : TrimErrorSuffix(symbol.ContainingType?.Name ?? string.Empty);
    }

    private static string? DeclaredPrefix(ISymbol symbol)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() == ErrorCatalogAttributeName
                && attribute.ConstructorArguments.Length > 0
                && attribute.ConstructorArguments[0].Value is string { Length: > 0 } prefix)
            {
                return prefix.TrimEnd('.');
            }
        }

        return null;
    }

    /// <summary><c>OrderErrors</c> becomes <c>Order</c>, because the suffix says nothing a code needs.</summary>
    private static string TrimErrorSuffix(string name)
    {
        foreach (var suffix in new[] { "Errors", "Error" })
        {
            if (name.Length > suffix.Length && name.EndsWith(suffix, StringComparison.Ordinal))
            {
                return name.Substring(0, name.Length - suffix.Length);
            }
        }

        return name;
    }

    /// <summary>Turns <c>AlreadyPaid</c> into <c>Already paid</c>, for a title nobody had to write.</summary>
    public static string Humanize(string name)
    {
        var words = SplitWords(name);
        if (words.Count == 0)
        {
            return name;
        }

        var builder = new StringBuilder(name.Length + words.Count);
        builder.Append(words[0]);

        for (var i = 1; i < words.Count; i++)
        {
            // Later words stay lower-case unless they are an acronym the author capitalised deliberately.
            var word = words[i];
            builder.Append(' ').Append(IsAcronym(word) ? word : word.ToLowerInvariant());
        }

        return builder.ToString();
    }

    private static bool IsAcronym(string word) => word.Length > 1 && word.All(char.IsUpper);

    private static List<string> SplitWords(string name)
    {
        var words = new List<string>();
        var current = new StringBuilder();

        foreach (var c in name)
        {
            if (c is '_' or '-' or '.')
            {
                Flush(words, current);
                continue;
            }

            if (char.IsUpper(c) && current.Length > 0 && !char.IsUpper(current[current.Length - 1]))
            {
                Flush(words, current);
            }

            current.Append(c);
        }

        Flush(words, current);
        return words;
    }

    private static void Flush(List<string> words, StringBuilder current)
    {
        if (current.Length > 0)
        {
            words.Add(current.ToString());
            current.Clear();
        }
    }
}
