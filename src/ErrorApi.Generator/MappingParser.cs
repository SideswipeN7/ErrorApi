using System.Collections.Generic;
using ErrorApi.Generator.Helpers;
using ErrorApi.Generator.Model;
using Microsoft.CodeAnalysis;

namespace ErrorApi.Generator;

/// <summary>
/// Reads <c>[assembly: ErrorMapping(typeof(X), …)]</c> into catalog entries.
/// </summary>
/// <remarks>
/// The declaration lives on the assembly rather than the type, because the type belongs to somebody
/// else. Everything downstream treats the result as an ordinary type-identified entry, so a mapped type
/// lands in the descriptor table, the instance switch and the TypeScript contract without a special case.
/// </remarks>
internal static class MappingParser
{
    public const string ErrorMappingAttributeName = "ErrorApi.ErrorMappingAttribute";

    public static List<CatalogEntry> Parse(Compilation compilation, List<DiagnosticInfo> diagnostics)
    {
        var entries = new List<CatalogEntry>();

        foreach (var attribute in compilation.Assembly.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != ErrorMappingAttributeName)
            {
                continue;
            }

            var node = attribute.ApplicationSyntaxReference?.GetSyntax();
            var arguments = attribute.ConstructorArguments;

            if (arguments.Length == 0 || arguments[0].Value is not INamedTypeSymbol errorType)
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    Diagnostics.InvalidCatalogMember, node, ErrorMappingAttributeName, "the mapped type could not be read"));
                continue;
            }

            string? declaredCode;
            int statusCode;

            switch (arguments.Length)
            {
                case 2 when arguments[1].Value is int statusOnly:
                    declaredCode = null;
                    statusCode = statusOnly;
                    break;

                case 3 when arguments[1].Value is string explicitCode && arguments[2].Value is int status:
                    declaredCode = explicitCode;
                    statusCode = status;
                    break;

                default:
                    diagnostics.Add(DiagnosticInfo.Create(
                        Diagnostics.InvalidCatalogMember,
                        node,
                        errorType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                        "the [ErrorMapping] arguments could not be read as (Type, int) or (Type, string, int)"));
                    continue;
            }

            var display = errorType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

            if (statusCode is < 100 or > 599)
            {
                diagnostics.Add(DiagnosticInfo.Create(Diagnostics.InvalidStatusCode, node, statusCode.ToString(), display));
                continue;
            }

            if (errorType.IsStatic || errorType.IsAbstract || errorType.TypeKind == TypeKind.Interface)
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    Diagnostics.InvalidCatalogMember, node, display, "a mapped type must be one you can hold an instance of"));
                continue;
            }

            string? title = null, detail = null, description = null;
            foreach (var named in attribute.NamedArguments)
            {
                var value = named.Value.Value as string;
                switch (named.Key)
                {
                    case "Title": title = value; break;
                    case "Detail": detail = value; break;
                    case "Description": description = value; break;
                }
            }

            entries.Add(new CatalogEntry(
                Kind: CatalogEntryKind.Declared,
                Code: declaredCode ?? NameInference.CodeFromName(errorType, isErrorType: true),
                StatusCode: statusCode,
                Title: title ?? NameInference.Humanize(NameInference.EntryName(errorType)),
                Detail: detail,
                Description: description,
                Namespace: errorType.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() : string.Empty,
                ContainerDeclarations: EquatableArray<string>.Empty,
                MemberModifiers: string.Empty,
                MemberName: errorType.Name,
                IsMethod: false,
                Parameters: EquatableArray<ParameterModel>.Empty,
                DeclaringMember: display,
                ErrorTypeDisplay: errorType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                Location: LocationInfo.From(node)));
        }

        return entries;
    }
}
