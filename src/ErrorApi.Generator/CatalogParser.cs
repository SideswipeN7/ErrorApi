using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using ErrorApi.Generator.Helpers;
using ErrorApi.Generator.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ErrorApi.Generator;

/// <summary>
/// Reads <c>[Error]</c> declarations into <see cref="CatalogEntry"/> models.
/// </summary>
/// <remarks>
/// A <c>static partial</c> member returning <see cref="ErrorTypeName"/> is implemented by the generator.
/// Everything else — a type, a field, a member with its own body — is recorded but not implemented, which
/// is how a catalog can be written in ErrorOr's, OneOf's or language-ext's own error types.
/// </remarks>
internal static class CatalogParser
{
    public const string ErrorAttributeName = "ErrorApi.ErrorAttribute";
    public const string ProducesErrorAttributeName = "ErrorApi.ProducesErrorAttribute";

    /// <summary>
    /// The diagnostic IDs a declaration silences with <c>[SuppressErrorApi]</c>. Generator diagnostics
    /// ignore <c>#pragma warning</c>, so this is the per-declaration lever.
    /// </summary>
    public static ImmutableHashSet<string> SuppressedIds(ISymbol? symbol)
    {
        if (symbol is null)
        {
            return ImmutableHashSet<string>.Empty;
        }

        foreach (var attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass is { Name: "SuppressErrorApiAttribute", ContainingNamespace: { Name: "ErrorApi" } ns }
                && ns.ContainingNamespace.IsGlobalNamespace
                && attribute.ConstructorArguments.Length == 1)
            {
                return attribute.ConstructorArguments[0].Values
                    .Select(v => v.Value as string)
                    .Where(v => v is not null)
                    .Select(v => v!)
                    .ToImmutableHashSet(System.StringComparer.Ordinal);
            }
        }

        return ImmutableHashSet<string>.Empty;
    }
    public const string ErrorTypeName = "ErrorApi.Error";

    public static ParsedCatalogEntry Parse(GeneratorAttributeSyntaxContext context) =>
        ParseCore(context.TargetSymbol, context.TargetNode, context.SemanticModel, context.Attributes[0]);

    /// <summary>
    /// <c>[ErrorCatalog]</c> on a type makes membership the declaration: every <c>static partial</c>
    /// member returning <see cref="ErrorTypeName"/> inside is an entry, no <c>[Error]</c> needed —
    /// the catalog names the prefix and (optionally) the status, the member names the code.
    /// Members that carry <c>[Error]</c> flow through <see cref="Parse"/> and are skipped here;
    /// a member with a hand-written implementation part stays the author's own.
    /// </summary>
    public static ImmutableArray<ParsedCatalogEntry> ParseCatalogType(GeneratorAttributeSyntaxContext context)
    {
        if (context.TargetSymbol is not INamedTypeSymbol type)
        {
            return ImmutableArray<ParsedCatalogEntry>.Empty;
        }

        var results = ImmutableArray.CreateBuilder<ParsedCatalogEntry>();

        foreach (var member in type.GetMembers())
        {
            var isImplicitEntry = member switch
            {
                IPropertySymbol { IsStatic: true, PartialImplementationPart: null } property =>
                    property.Type.ToDisplayString() == ErrorTypeName,
                IMethodSymbol { IsStatic: true, MethodKind: MethodKind.Ordinary, IsPartialDefinition: true, PartialImplementationPart: null } method =>
                    method.ReturnType.ToDisplayString() == ErrorTypeName,
                _ => false,
            };

            if (!isImplicitEntry || HasErrorAttribute(member))
            {
                continue;
            }

            foreach (var reference in member.DeclaringSyntaxReferences)
            {
                if (reference.GetSyntax() is not MemberDeclarationSyntax node
                    || !node.Modifiers.Any(SyntaxKind.PartialKeyword))
                {
                    continue;
                }

                var model = node.SyntaxTree == context.SemanticModel.SyntaxTree
                    ? context.SemanticModel
                    : context.SemanticModel.Compilation.GetSemanticModel(node.SyntaxTree);

                results.Add(ParseCore(member, node, model, attribute: null));
                break;
            }
        }

        return results.ToImmutable();
    }

    private static bool HasErrorAttribute(ISymbol symbol) =>
        symbol.GetAttributes().Any(a =>
            a.AttributeClass is { Name: "ErrorAttribute", ContainingNamespace: { Name: "ErrorApi" } ns }
            && ns.ContainingNamespace.IsGlobalNamespace);

    private static ParsedCatalogEntry ParseCore(ISymbol symbol, SyntaxNode node, SemanticModel semanticModel, AttributeData? attribute)
    {
        var display = symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

        // [Error] infers everything; [Error(404)] leaves the code to be inferred;
        // [Error("Orders.NotFound", 404)] spells both out. No attribute at all is the implicit form:
        // a partial Error member claimed by its [ErrorCatalog] type.
        var arguments = attribute?.ConstructorArguments ?? ImmutableArray<TypedConstant>.Empty;
        string? declaredCode;
        int? statusCode;

        switch (arguments.Length)
        {
            case 0:
                declaredCode = null;
                statusCode = null;
                break;

            case 1 when arguments[0].Value is int statusOnly:
                declaredCode = null;
                statusCode = statusOnly;
                break;

            case 2 when arguments[0].Value is string explicitCode && arguments[1].Value is int status:
                declaredCode = explicitCode;
                statusCode = status;
                break;

            default:
                return Invalid(node, display, "the [Error] arguments could not be read as (), (int statusCode) or (string code, int statusCode)");
        }

        string? title = null, detail = null, description = null;
        foreach (var named in attribute?.NamedArguments ?? ImmutableArray<KeyValuePair<string, TypedConstant>>.Empty)
        {
            var value = named.Value.Value as string;
            switch (named.Key)
            {
                case "Title": title = value; break;
                case "Detail": detail = value; break;
                case "Description": description = value; break;
            }
        }

        // The most specific declaration wins: [ErrorStatusCode]/[ErrorDescription] on the member beat
        // the [Error] arguments, which beat the catalog's default, which beats the base constructor.
        statusCode = NameInference.OverrideStatus(symbol) ?? statusCode ?? NameInference.CatalogDefaultStatus(symbol);
        description = NameInference.OverrideDescription(symbol) ?? description;

        string? inferredTitle = null;
        var statusFromSource = false;

        if (statusCode is null && symbol is INamedTypeSymbol)
        {
            // The library the type extends may already carry the data — Expected("Order not found", 404).
            (statusCode, inferredTitle) = NameInference.StatusFromBase(node, semanticModel);
            statusFromSource = statusCode is not null;
        }

        if (statusCode is null)
        {
            return Invalid(
                node,
                display,
                "the entry has no status code: give it [Error(statusCode)] or [ErrorStatusCode], set a catalog default with [ErrorCatalog(prefix, statusCode)], or (on a type) pass it in the base constructor");
        }

        if (statusCode is < 100 or > 599)
        {
            return new ParsedCatalogEntry(null, DiagnosticInfo.Create(Diagnostics.InvalidStatusCode, node, statusCode.Value.ToString(), display));
        }

        var isErrorType = symbol is INamedTypeSymbol;
        var bodyCode = NameInference.CodeFromBody(node, semanticModel);

        var code = declaredCode ?? bodyCode ?? NameInference.CodeFromName(symbol, isErrorType);
        title ??= inferredTitle ?? NameInference.Humanize(NameInference.EntryName(symbol));

        // A code written twice is a code that can drift, and the half nobody reads is the documented one.
        DiagnosticInfo? drift = declaredCode is not null && bodyCode is not null && bodyCode != declaredCode
            ? DiagnosticInfo.Create(Diagnostics.CodeDisagreesWithBody, node, declaredCode, bodyCode)
            : null;

        // What was resolved from source cannot be re-derived from metadata, so it is exported for
        // consumers: a body-inferred code, and a base-constructor-inferred status.
        var exportId = (declaredCode is null && bodyCode is not null) || statusFromSource
            ? symbol.GetDocumentationCommentId()
            : null;

        var parsed = symbol is INamedTypeSymbol type
            ? ParseErrorType(type, node, code, statusCode.Value, title, detail, description, exportId, statusFromSource)
            : ParseMember(symbol, node, display, code, statusCode.Value, title, detail, description, exportId);

        var suppressed = SuppressedIds(symbol);
        if (parsed.Entry is { } built && !suppressed.IsEmpty)
        {
            parsed = parsed with
            {
                Entry = built with
                {
                    Suppressions = new EquatableArray<string>(
                        suppressed.OrderBy(s => s, System.StringComparer.Ordinal).ToImmutableArray()),
                },
            };
        }

        return drift is null || parsed.Diagnostic is not null ? parsed : parsed with { Diagnostic = drift };
    }

    /// <summary>
    /// <c>[Error]</c> on a type: the type itself identifies the failure. Any union — OneOf, a
    /// language-ext error, a hand-rolled closed hierarchy — becomes a catalog entry this way.
    /// </summary>
    private static ParsedCatalogEntry ParseErrorType(
        INamedTypeSymbol type, SyntaxNode node, string code, int statusCode, string? title, string? detail, string? description,
        string? exportId, bool statusFromSource)
    {
        var display = type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

        if (type.IsStatic)
        {
            return Invalid(node, display, "a static type cannot be instantiated, so it cannot identify a failure");
        }

        if (type.IsAbstract)
        {
            return Invalid(node, display, "an abstract type cannot identify a single failure; annotate the concrete cases instead");
        }

        return new ParsedCatalogEntry(
            new CatalogEntry(
                Kind: CatalogEntryKind.Declared,
                Code: code,
                StatusCode: statusCode,
                Title: title,
                Detail: detail,
                Description: description,
                Namespace: NamespaceOf(type),
                ContainerDeclarations: EquatableArray<string>.Empty,
                MemberModifiers: string.Empty,
                MemberName: type.Name,
                IsMethod: false,
                Parameters: EquatableArray<ParameterModel>.Empty,
                DeclaringMember: type.ToDisplayString(FullMemberFormat),
                ErrorTypeDisplay: type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                Location: LocationInfo.From(node),
                ExportId: exportId,
                ExportsStatus: statusFromSource),
            null);
    }

    private static ParsedCatalogEntry ParseMember(
        ISymbol symbol, SyntaxNode node, string display, string code, int statusCode, string? title, string? detail, string? description, string? exportId)
    {
        var member = node as MemberDeclarationSyntax
                     ?? node.Ancestors().OfType<MemberDeclarationSyntax>().FirstOrDefault();

        // Only a member that asks for generation with `partial` is held to the generated-member rules.
        var wantsGeneration = member is not null && member.Modifiers.Any(SyntaxKind.PartialKeyword);

        var returnType = symbol switch
        {
            IPropertySymbol property => property.Type,
            IMethodSymbol method => method.ReturnType,
            IFieldSymbol field => field.Type,
            _ => null,
        };

        var containingNamespace = NamespaceOf(symbol);

        if (!wantsGeneration)
        {
            return new ParsedCatalogEntry(
                new CatalogEntry(
                    Kind: CatalogEntryKind.Declared,
                    Code: code,
                    StatusCode: statusCode,
                    Title: title,
                    Detail: detail,
                    Description: description,
                    Namespace: containingNamespace,
                    ContainerDeclarations: EquatableArray<string>.Empty,
                    MemberModifiers: string.Empty,
                    MemberName: symbol.Name,
                    IsMethod: symbol is IMethodSymbol,
                    Parameters: EquatableArray<ParameterModel>.Empty,
                    DeclaringMember: symbol.ToDisplayString(FullMemberFormat),
                    ErrorTypeDisplay: null,
                    Location: LocationInfo.From(node),
                    ExportId: exportId),
                null);
        }

        if (!member!.Modifiers.Any(SyntaxKind.StaticKeyword))
        {
            return Invalid(node, display, "a partial catalog member must be declared 'static'");
        }

        if (returnType is null || returnType.ToDisplayString() != ErrorTypeName)
        {
            return Invalid(node, display, $"a partial catalog member must return '{ErrorTypeName}'");
        }

        if (node is PropertyDeclarationSyntax { AccessorList: { } accessors }
            && accessors.Accessors.Any(a => a.Body is not null || a.ExpressionBody is not null))
        {
            return Invalid(node, display, "the declaring part must not implement its accessors");
        }

        var containers = new List<string>();
        foreach (var containingType in node.Ancestors().OfType<TypeDeclarationSyntax>().Reverse())
        {
            if (!containingType.Modifiers.Any(SyntaxKind.PartialKeyword))
            {
                return Invalid(node, display, $"the containing type '{containingType.Identifier.ValueText}' must be declared 'partial'");
            }

            containers.Add(DeclarationHeader(containingType));
        }

        if (containers.Count == 0)
        {
            return Invalid(node, display, "it must be declared inside a partial type");
        }

        var parameters = symbol is IMethodSymbol methodSymbol
            ? methodSymbol.Parameters
                .Select(p => new ParameterModel(p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), p.Name))
                .ToImmutableArray()
            : ImmutableArray<ParameterModel>.Empty;

        return new ParsedCatalogEntry(
            new CatalogEntry(
                Kind: CatalogEntryKind.Generated,
                Code: code,
                StatusCode: statusCode,
                Title: title,
                Detail: detail,
                Description: description,
                Namespace: containingNamespace,
                ContainerDeclarations: new EquatableArray<string>(containers.ToImmutableArray()),
                MemberModifiers: string.Join(" ", member.Modifiers.Select(m => m.ValueText)),
                MemberName: symbol.Name,
                IsMethod: symbol is IMethodSymbol,
                Parameters: new EquatableArray<ParameterModel>(parameters),
                DeclaringMember: symbol.ToDisplayString(FullMemberFormat),
                ErrorTypeDisplay: null,
                Location: LocationInfo.From(node)),
            null);
    }

    private static string NamespaceOf(ISymbol symbol) =>
        symbol.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() : string.Empty;

    private static readonly SymbolDisplayFormat FullMemberFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        memberOptions: SymbolDisplayMemberOptions.IncludeContainingType,
        parameterOptions: SymbolDisplayParameterOptions.None);

    private static string DeclarationHeader(TypeDeclarationSyntax type)
    {
        var modifiers = string.Join(" ", type.Modifiers.Select(m => m.ValueText));
        var keyword = type.Keyword.ValueText;
        if (type is RecordDeclarationSyntax { ClassOrStructKeyword.ValueText: { Length: > 0 } extra })
        {
            keyword += " " + extra;
        }

        var typeParameters = type.TypeParameterList?.ToString() ?? string.Empty;
        return $"{modifiers} {keyword} {type.Identifier.ValueText}{typeParameters}".Trim();
    }

    private static ParsedCatalogEntry Invalid(SyntaxNode node, string display, string reason) =>
        new(null, DiagnosticInfo.Create(Diagnostics.InvalidCatalogMember, node, display, reason));
}

/// <summary>The outcome of parsing a single <c>[Error]</c> declaration.</summary>
internal sealed record ParsedCatalogEntry(CatalogEntry? Entry, DiagnosticInfo? Diagnostic);

