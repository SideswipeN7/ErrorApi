using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using ErrorApi.Generator.Emit;
using ErrorApi.Generator.Helpers;
using ErrorApi.Generator.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ErrorApi.Generator;

/// <summary>
/// Turns an <c>[Error]</c>-annotated catalog plus the Minimal API <c>Map*</c> calls of a compilation
/// into three things: the catalog implementation, a reflection-free error model, and the endpoint
/// contract that the OpenAPI document and the TypeScript client are rendered from.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ErrorApiGenerator : IIncrementalGenerator
{
    private const string MetadataInterfaceName = "ErrorApi.IErrorApiMetadata";
    private const string RegistrationTypeName = "ErrorApi.AspNetCore.ErrorApiRegistration";

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var catalog = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                CatalogParser.ErrorAttributeName,
                static (node, _) => node is PropertyDeclarationSyntax or MethodDeclarationSyntax
                                            or TypeDeclarationSyntax or VariableDeclaratorSyntax,
                static (ctx, _) => CatalogParser.Parse(ctx))
            .Collect();

        var mapCalls = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax member }
                                    && EndpointScanner.IsCandidateName(member.Name.Identifier.ValueText),
                static (ctx, _) => (InvocationExpressionSyntax)ctx.Node)
            .Collect();

        var input = context.CompilationProvider.Combine(catalog).Combine(mapCalls)
            .Combine(context.AnalyzerConfigOptionsProvider);

        context.RegisterSourceOutput(
            input,
            static (spc, data) => Execute(spc, data.Left.Left.Left, data.Left.Left.Right, data.Left.Right, data.Right));
    }

    private static void Execute(
        SourceProductionContext context,
        Compilation compilation,
        ImmutableArray<ParsedCatalogEntry> parsed,
        ImmutableArray<InvocationExpressionSyntax> mapCalls,
        AnalyzerConfigOptionsProvider configuration)
    {
        if (compilation.GetTypeByMetadataName(MetadataInterfaceName) is null)
        {
            // ErrorApi.Abstractions is not referenced; there is nothing this generator can legally emit.
            return;
        }

        var diagnostics = new List<DiagnosticInfo>();
        var entries = CollectCatalog(parsed, diagnostics);

        var scan = EndpointScanner.Scan(compilation, mapCalls, configuration, diagnostics);
        var errors = MergeErrors(entries, scan.DiscoveredErrors);
        ReportUnknownCodes(scan.Endpoints, errors, diagnostics);

        foreach (var (hintName, source) in CatalogEmitter.Emit(entries))
        {
            context.AddSource(hintName, source);
        }

        context.AddSource(MetadataEmitter.HintName, MetadataEmitter.Emit(errors, scan.Endpoints, entries));

        if (compilation.GetTypeByMetadataName(RegistrationTypeName) is not null)
        {
            context.AddSource(MetadataEmitter.RegistrationHintName, MetadataEmitter.EmitRegistration());
        }

        foreach (var diagnostic in diagnostics)
        {
            context.ReportDiagnostic(diagnostic.ToDiagnostic());
        }
    }

    private static List<CatalogEntry> CollectCatalog(ImmutableArray<ParsedCatalogEntry> parsed, List<DiagnosticInfo> diagnostics)
    {
        var byCode = new Dictionary<string, CatalogEntry>(System.StringComparer.Ordinal);
        var entries = new List<CatalogEntry>();

        foreach (var candidate in parsed)
        {
            if (candidate.Diagnostic is not null)
            {
                diagnostics.Add(candidate.Diagnostic);
            }

            if (candidate.Entry is not { } entry)
            {
                continue;
            }

            if (byCode.TryGetValue(entry.Code, out var existing))
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    Diagnostics.DuplicateErrorCode, entry.Location, entry.Code, existing.DeclaringMember));
                continue;
            }

            byCode[entry.Code] = entry;
            entries.Add(entry);
        }

        return entries;
    }

    /// <summary>
    /// Unions the catalog declared here with catalog entries reached through referenced assemblies,
    /// so an app that consumes a shared error catalog still documents it in full.
    /// </summary>
    private static List<DiscoveredError> MergeErrors(IReadOnlyList<CatalogEntry> entries, IReadOnlyList<DiscoveredError> discovered)
    {
        var byCode = new Dictionary<string, DiscoveredError>(System.StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            byCode[entry.Code] = new DiscoveredError(
                entry.Code, entry.StatusCode, entry.Title, DocumentationDetail(entry), entry.Description, entry.DeclaringMember);
        }

        foreach (var error in discovered)
        {
            if (!byCode.ContainsKey(error.Code))
            {
                byCode[error.Code] = error;
            }
        }

        return byCode.Values.OrderBy(e => e.Code, System.StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Rewrites a detail template from positional placeholders to the parameter names it binds to,
    /// so <c>Order {0} was already paid</c> reads as <c>Order {orderId} was already paid</c> in the
    /// OpenAPI example. The runtime value still goes through <see cref="string.Format(string, object[])"/>.
    /// </summary>
    private static string? DocumentationDetail(CatalogEntry entry)
    {
        if (entry.Detail is null || !entry.IsMethod || entry.Parameters.Count == 0)
        {
            return entry.Detail;
        }

        var detail = entry.Detail;
        for (var i = 0; i < entry.Parameters.Count; i++)
        {
            detail = detail.Replace("{" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}", "{" + entry.Parameters[i].Name + "}");
        }

        return detail;
    }

    private static void ReportUnknownCodes(
        IReadOnlyList<EndpointModel> endpoints,
        IReadOnlyList<DiscoveredError> errors,
        List<DiagnosticInfo> diagnostics)
    {
        var known = new HashSet<string>(errors.Select(e => e.Code), System.StringComparer.Ordinal);
        var reported = new HashSet<string>(System.StringComparer.Ordinal);

        foreach (var endpoint in endpoints)
        {
            foreach (var code in endpoint.ErrorCodes)
            {
                if (!known.Contains(code) && reported.Add(code))
                {
                    diagnostics.Add(DiagnosticInfo.Create(Diagnostics.UnknownErrorCode, endpoint.Location, code));
                }
            }
        }
    }
}
