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
        ISymbol symbol, AttributeData? attribute, Compilation compilation, Func<SyntaxTree, SemanticModel> semanticModel)
    {
        if (attribute is { ConstructorArguments.Length: 2 } && attribute.ConstructorArguments[0].Value is string declared)
        {
            return declared;
        }

        // A referenced catalog resolved its body-inferred codes when it was compiled and exported the
        // result; reading that back is the only way this compilation can agree with what is on the wire.
        if (!SymbolEqualityComparer.Default.Equals(symbol.ContainingAssembly, compilation.Assembly)
            && ExportedCode(symbol) is { } exported)
        {
            return exported;
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

    /// <summary>The code the symbol's own assembly exported for it, if any.</summary>
    private static string? ExportedCode(ISymbol symbol)
    {
        if (symbol.ContainingAssembly is not { } assembly)
        {
            return null;
        }

        string? memberId = null;

        foreach (var attribute in assembly.GetAttributes())
        {
            if (attribute.AttributeClass is not { Name: "CatalogExportAttribute" } cls
                || cls.ContainingNamespace is not { Name: "ErrorApi" } ns
                || !ns.ContainingNamespace.IsGlobalNamespace
                || attribute.ConstructorArguments.Length < 2)
            {
                continue;
            }

            memberId ??= symbol.GetDocumentationCommentId();
            if (attribute.ConstructorArguments[0].Value is string id && id == memberId
                && attribute.ConstructorArguments[1].Value is string code)
            {
                return code;
            }
        }

        return null;
    }

    /// <summary>The status (and title) the symbol's own assembly exported, when the full form was baked.</summary>
    public static (int StatusCode, string? Title)? ExportedStatus(ISymbol symbol)
    {
        if (symbol.ContainingAssembly is not { } assembly)
        {
            return null;
        }

        string? memberId = null;

        foreach (var attribute in assembly.GetAttributes())
        {
            if (attribute.AttributeClass is not { Name: "CatalogExportAttribute" } cls
                || cls.ContainingNamespace is not { Name: "ErrorApi" } ns
                || !ns.ContainingNamespace.IsGlobalNamespace
                || attribute.ConstructorArguments.Length < 4)
            {
                continue;
            }

            memberId ??= symbol.GetDocumentationCommentId();
            if (attribute.ConstructorArguments[0].Value is string id && id == memberId
                && attribute.ConstructorArguments[2].Value is int status && status is >= 100 and <= 599)
            {
                return (status, attribute.ConstructorArguments[3].Value as string);
            }
        }

        return null;
    }

    /// <summary>The <c>[ErrorStatusCode]</c> override, when declared. The most specific status always wins.</summary>
    public static int? OverrideStatus(ISymbol symbol)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass is { Name: "ErrorStatusCodeAttribute", ContainingNamespace: { Name: "ErrorApi" } ns }
                && ns.ContainingNamespace.IsGlobalNamespace
                && attribute.ConstructorArguments.Length == 1
                && attribute.ConstructorArguments[0].Value is int status)
            {
                return status;
            }
        }

        return null;
    }

    /// <summary>The <c>[ErrorDescription]</c> override, when declared.</summary>
    public static string? OverrideDescription(ISymbol symbol)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass is { Name: "ErrorDescriptionAttribute", ContainingNamespace: { Name: "ErrorApi" } ns }
                && ns.ContainingNamespace.IsGlobalNamespace
                && attribute.ConstructorArguments.Length == 1
                && attribute.ConstructorArguments[0].Value is string description)
            {
                return description;
            }
        }

        return null;
    }

    /// <summary>
    /// The status an entry inherits from its catalog —
    /// <c>[ErrorCatalog("Order.Validation", 422)]</c> on a containing type, or on the assembly.
    /// Nearest declaration wins, the same walk the prefix takes.
    /// </summary>
    public static int? CatalogDefaultStatus(ISymbol symbol)
    {
        for (var type = symbol is INamedTypeSymbol self ? self.ContainingType : symbol.ContainingType;
             type is not null;
             type = type.ContainingType)
        {
            if (DeclaredDefaultStatus(type) is { } declared)
            {
                return declared;
            }
        }

        return symbol.ContainingAssembly is { } assembly ? DeclaredDefaultStatus(assembly) : null;
    }

    private static int? DeclaredDefaultStatus(ISymbol symbol)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() == ErrorCatalogAttributeName
                && attribute.ConstructorArguments.Length == 2
                && attribute.ConstructorArguments[1].Value is int status
                && status is >= 100 and <= 599)
            {
                return status;
            }
        }

        return null;
    }

    /// <summary>Parameter names a library uses for the HTTP-status-like slot of its error type.</summary>
    private static readonly string[] StatusParameterNames = ["status", "statusCode", "httpStatus", "httpStatusCode", "code"];

    /// <summary>
    /// Reads the status — and a title, when a string literal sits beside it — out of an annotated
    /// type's base constructor call. This is the shape of a library that already carries the data:
    /// <c>record NotFound(Guid Id) : Expected("Order not found", 404)</c> has said everything an
    /// <c>[Error]</c> needs, and repeating it in the attribute is the duplication this exists to remove.
    /// The int is matched by its <em>parameter name</em> (<c>code</c>, <c>status</c>, …), so a base
    /// constructor with an unrelated in-range int — a version, a size — never mis-infers; only when
    /// the constructor cannot be resolved does a single unambiguous in-range literal count.
    /// </summary>
    public static (int? StatusCode, string? Title) StatusFromBase(SyntaxNode declaration, SemanticModel model)
    {
        (SeparatedSyntaxList<ArgumentSyntax> Arguments, SyntaxNode Target)? call = declaration switch
        {
            TypeDeclarationSyntax { BaseList: { } bases } when bases.Types
                .OfType<PrimaryConstructorBaseTypeSyntax>()
                .FirstOrDefault() is { } primary => (primary.ArgumentList.Arguments, primary),
            _ => Initializer(declaration),
        };

        if (call is not { } found)
        {
            return (null, null);
        }

        var constructor = model.GetSymbolInfo(found.Target).Symbol as IMethodSymbol;

        int? status = null;
        string? title = null;
        var inRange = new List<int>();

        for (var i = 0; i < found.Arguments.Count; i++)
        {
            var argument = found.Arguments[i];
            var constant = model.GetConstantValue(argument.Expression).Value;

            if (constant is int value && value is >= 100 and <= 599)
            {
                inRange.Add(value);

                var parameter = argument.NameColon is { } named
                    ? constructor?.Parameters.FirstOrDefault(p => p.Name == named.Name.Identifier.ValueText)
                    : constructor is not null && i < constructor.Parameters.Length
                        ? constructor.Parameters[i]
                        : null;

                if (parameter is not null
                    && StatusParameterNames.Contains(parameter.Name, StringComparer.OrdinalIgnoreCase))
                {
                    status ??= value;
                }
            }
            else if (constant is string { Length: > 0 } text)
            {
                title ??= text;
            }
        }

        // With no constructor to name the slots, a lone in-range literal is still unambiguous.
        if (status is null && constructor is null && inRange.Count == 1)
        {
            status = inRange[0];
        }

        return (status, status is null ? null : title);
    }

    private static (SeparatedSyntaxList<ArgumentSyntax> Arguments, SyntaxNode Target)? Initializer(SyntaxNode declaration)
    {
        // A class without a primary constructor passes base arguments through `: base(...)`.
        foreach (var constructor in declaration.DescendantNodes().OfType<ConstructorDeclarationSyntax>())
        {
            if (constructor.Initializer is { } initializer
                && initializer.ThisOrBaseKeyword.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.BaseKeyword))
            {
                return (initializer.ArgumentList.Arguments, initializer);
            }
        }

        return null;
    }

    /// <summary>
    /// Builds a wire code from the declaration's own name. A member takes its catalog's prefix; an
    /// annotated type takes a prefix only when one is declared for it, since its name already reads as
    /// an identity.
    /// </summary>
    public static string CodeFromName(ISymbol symbol, bool isErrorType)
    {
        var name = EntryName(symbol);
        var prefix = Prefix(symbol, isErrorType);
        return prefix.Length == 0 ? name : prefix + "." + name;
    }

    /// <summary>
    /// The declaration's name with the noise a wire code does not need. A client switching on
    /// <c>Orders.NotFound</c> does not care that the server models it as an exception, and
    /// <c>NotFoundError</c> in an error catalog says "error" twice.
    /// </summary>
    public static string EntryName(ISymbol symbol) => TrimSuffix(symbol.Name, "Exception", "Error");

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
    private static string TrimErrorSuffix(string name) => TrimSuffix(name, "Errors", "Error");

    private static string TrimSuffix(string name, params string[] suffixes)
    {
        foreach (var suffix in suffixes)
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
