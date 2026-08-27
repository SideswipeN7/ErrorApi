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

    /// <summary>The tracking name of the model stage, so tests can watch it cache.</summary>
    public const string ModelStepName = "ErrorApi.Model";

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

        // The walk has to see the whole compilation, so it re-runs on every edit — but it funnels into
        // one value-equatable model here, which means an edit that does not change the outcome leaves
        // the emit step cached: no re-added sources, no re-parsed generated files in the IDE.
        var model = input.Select(static (data, cancellationToken) =>
                Build(data.Left.Left.Left, data.Left.Left.Right, data.Left.Right, data.Right, cancellationToken))
            .WithTrackingName(ModelStepName);

        context.RegisterSourceOutput(model, static (spc, result) => Emit(spc, result));
    }

    private static GenerationModel Build(
        Compilation compilation,
        ImmutableArray<ParsedCatalogEntry> parsed,
        ImmutableArray<InvocationExpressionSyntax> mapCalls,
        AnalyzerConfigOptionsProvider configuration,
        System.Threading.CancellationToken cancellationToken)
    {
        if (compilation.GetTypeByMetadataName(MetadataInterfaceName) is null)
        {
            // ErrorApi.Abstractions is not referenced; there is nothing this generator can legally emit.
            return GenerationModel.Empty;
        }

        var diagnostics = new List<DiagnosticInfo>();

        // Mappings first: an entry attached from the outside is still an entry, and pushing both through
        // one dedup is what makes a clash between them report as EAPI001 rather than pick a winner.
        var entries = CollectCatalog(parsed, MappingParser.Parse(compilation, diagnostics), diagnostics);

        var mappedTypes = entries
            .Where(e => e.ErrorTypeDisplay is not null)
            .GroupBy(e => e.ErrorTypeDisplay!, System.StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Code, System.StringComparer.Ordinal);

        var scan = EndpointScanner.Scan(compilation, mapCalls, configuration, mappedTypes, diagnostics, cancellationToken);
        var errors = MergeErrors(entries, scan.DiscoveredErrors);
        ReportUnknownCodes(scan.Endpoints, errors, diagnostics);
        ReportUnreachableErrors(entries, scan.Endpoints, diagnostics);

        // A compilation with no endpoints is a library: its walk starts at its own public surface, and
        // the result is baked in for the compilation that has the endpoints to read back.
        var reachability = ExportsReachability(configuration, compilation, hasEndpoints: scan.Endpoints.Count > 0)
            ? ReachabilityExporter.Compute(compilation, mappedTypes, cancellationToken)
            : new List<ReachabilityExport>();

        return new GenerationModel(
            HasAbstractions: true,
            HasRegistrationType: compilation.GetTypeByMetadataName(RegistrationTypeName) is not null,
            Entries: entries.ToEquatableArray(),
            Errors: errors.ToEquatableArray(),
            Endpoints: scan.Endpoints.ToEquatableArray(),
            Diagnostics: diagnostics.ToEquatableArray(),
            Reachability: reachability.ToEquatableArray());
    }

    /// <summary>
    /// Whether this compilation exports its reachability. On by default for a compilation with no
    /// endpoints — that is a library, and its whole point of running the generator is to be consumed;
    /// <c>errorapi_export_reachability</c> in .editorconfig overrides in either direction.
    /// </summary>
    private static bool ExportsReachability(AnalyzerConfigOptionsProvider configuration, Compilation compilation, bool hasEndpoints)
    {
        var tree = compilation.SyntaxTrees.FirstOrDefault();
        if (tree is not null
            && configuration.GetOptions(tree).TryGetValue("errorapi_export_reachability", out var value)
            && bool.TryParse(value, out var declared))
        {
            return declared;
        }

        return !hasEndpoints;
    }

    private static void Emit(SourceProductionContext context, GenerationModel model)
    {
        if (!model.HasAbstractions)
        {
            return;
        }

        var entries = model.Entries.AsImmutableArray();

        foreach (var (hintName, source) in CatalogEmitter.Emit(entries))
        {
            context.AddSource(hintName, source);
        }

        context.AddSource(
            MetadataEmitter.HintName,
            MetadataEmitter.Emit(
                model.Errors.AsImmutableArray(),
                model.Endpoints.AsImmutableArray(),
                entries,
                model.Reachability.AsImmutableArray()));

        if (model.HasRegistrationType)
        {
            context.AddSource(MetadataEmitter.RegistrationHintName, MetadataEmitter.EmitRegistration());
        }

        foreach (var diagnostic in model.Diagnostics)
        {
            context.ReportDiagnostic(diagnostic.ToDiagnostic());
        }
    }

    private static List<CatalogEntry> CollectCatalog(
        ImmutableArray<ParsedCatalogEntry> parsed,
        IEnumerable<CatalogEntry> mapped,
        List<DiagnosticInfo> diagnostics)
    {
        var byCode = new Dictionary<string, CatalogEntry>(System.StringComparer.Ordinal);
        var entries = new List<CatalogEntry>();

        foreach (var entry in mapped)
        {
            if (byCode.TryGetValue(entry.Code, out var clash))
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    Diagnostics.DuplicateErrorCode, entry.Location, entry.Code, clash.DeclaringMember));
                continue;
            }

            byCode[entry.Code] = entry;
            entries.Add(entry);
        }

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

    /// <summary>
    /// Reports catalog entries that no endpoint can return.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two things produce this, and they want opposite fixes: the entry is dead and should be deleted,
    /// or it is raised behind a boundary the walk cannot cross and the endpoints need
    /// <c>[ProducesError]</c>. The second is why this rule earns its keep — a contract that quietly lost
    /// half its failures shows up here as codes nobody documents.
    /// </para>
    /// <para>
    /// Only entries declared in this compilation are checked; one discovered through the walk is used by
    /// definition. A compilation with no endpoints is not an API, so a shared catalog project stays quiet.
    /// </para>
    /// </remarks>
    private static void ReportUnreachableErrors(
        IReadOnlyList<CatalogEntry> entries,
        IReadOnlyList<EndpointModel> endpoints,
        List<DiagnosticInfo> diagnostics)
    {
        if (endpoints.Count == 0)
        {
            return;
        }

        var reachable = new HashSet<string>(
            endpoints.SelectMany(endpoint => endpoint.ErrorCodes), System.StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            if (!reachable.Contains(entry.Code) && !entry.Suppressions.Contains(Diagnostics.UnreachableError.Id))
            {
                diagnostics.Add(DiagnosticInfo.Create(Diagnostics.UnreachableError, entry.Location, entry.Code));
            }
        }
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
