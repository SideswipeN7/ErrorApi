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
    private const string ExceptionOptionsTypeName = "ErrorApi.AspNetCore.ErrorApiExceptionOptions";
    private const string HandlingBuilderTypeName = "ErrorApi.AspNetCore.ExceptionHandlingBuilder";
    private const string OptionsTypeName = "ErrorApi.AspNetCore.ErrorApiOptions";
    private const string FallbackMethodName = "MapUnhandledException";
    private const string HandlingMethodName = "HandleExceptions";
    private const string AddHandlerMethodName = "Add";

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

        // The implicit form: [ErrorCatalog] on a type claims every static partial Error member inside,
        // no [Error] required. Members that do carry [Error] flow through the provider above and are
        // skipped here, so nothing is parsed twice.
        var implicitCatalog = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                Helpers.NameInference.ErrorCatalogAttributeName,
                static (node, _) => node is TypeDeclarationSyntax,
                static (ctx, _) => CatalogParser.ParseCatalogType(ctx))
            .SelectMany(static (entries, _) => entries)
            .Collect();

        var mapCalls = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax member }
                                    && EndpointScanner.IsCandidateName(member.Name.Identifier.ValueText),
                static (ctx, _) => (InvocationExpressionSyntax)ctx.Node)
            .Collect();

        // The unhandled-exception fallback: MapUnhandledException(ApiErrors.Failed) inside the options
        // lambda. Its argument is a catalog read like any other, and the entry is reachable from every
        // endpoint by definition — so it is resolved here and documented everywhere, at compile time.
        var fallbackCalls = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax member }
                                    && member.Name.Identifier.ValueText == FallbackMethodName,
                static (ctx, _) => (InvocationExpressionSyntax)ctx.Node)
            .Collect();

        // The global handlers: HandleExceptions(h => h.Add<SqlHandler>().Add(e => ...)). Each handler is
        // reachable from every endpoint too, so its Map method — or the lambda — is walked like a handler
        // and what it reads lands on every contract.
        var handlingCalls = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax member }
                                    && member.Name.Identifier.ValueText == HandlingMethodName,
                static (ctx, _) => (InvocationExpressionSyntax)ctx.Node)
            .Collect();

        var input = context.CompilationProvider.Combine(catalog).Combine(implicitCatalog).Combine(mapCalls)
            .Combine(fallbackCalls)
            .Combine(handlingCalls)
            .Combine(context.AnalyzerConfigOptionsProvider);

        // The walk has to see the whole compilation, so it re-runs on every edit — but it funnels into
        // one value-equatable model here, which means an edit that does not change the outcome leaves
        // the emit step cached: no re-added sources, no re-parsed generated files in the IDE.
        var model = input.Select(static (data, cancellationToken) =>
                Build(
                    data.Left.Left.Left.Left.Left.Left,
                    data.Left.Left.Left.Left.Left.Right.AddRange(data.Left.Left.Left.Left.Right),
                    data.Left.Left.Left.Right,
                    data.Left.Left.Right,
                    data.Left.Right,
                    data.Right,
                    cancellationToken))
            .WithTrackingName(ModelStepName);

        context.RegisterSourceOutput(model, static (spc, result) => Emit(spc, result));
    }

    private static GenerationModel Build(
        Compilation compilation,
        ImmutableArray<ParsedCatalogEntry> parsed,
        ImmutableArray<InvocationExpressionSyntax> mapCalls,
        ImmutableArray<InvocationExpressionSyntax> fallbackCalls,
        ImmutableArray<InvocationExpressionSyntax> handlingCalls,
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

        // One walker serves the endpoint scan and the reachability export alike, so its caches — source
        // types, semantic models, handler and implementation lookups — are built once per build.
        var walker = new ErrorReachabilityWalker(compilation)
        {
            MappedTypes = mappedTypes,
            ForeignAssemblyFilter = IncludeAssemblies(configuration),
        };

        // Resolved before the scan so the walker registers what the fallback and the global handlers
        // reach as discovered alongside everything the scan finds; appended after it so every contract
        // carries them, EAPI010 included.
        var globalCodes = ResolveGlobalErrors(compilation, fallbackCalls, handlingCalls, walker, diagnostics, cancellationToken);

        var scan = EndpointScanner.Scan(compilation, mapCalls, configuration, walker, diagnostics, cancellationToken);
        var endpoints = globalCodes.Count == 0 ? scan.Endpoints : AppendToEvery(scan.Endpoints, globalCodes);
        var errors = MergeErrors(entries, scan.DiscoveredErrors);
        ReportUnknownCodes(endpoints, errors, diagnostics);
        ReportUnreachableErrors(entries, endpoints, diagnostics);

        // A compilation with no endpoints is a library: its walk starts at its own public surface, and
        // the result is baked in for the compilation that has the endpoints to read back.
        var reachability = ExportsReachability(configuration, compilation, hasEndpoints: endpoints.Count > 0)
            ? ReachabilityExporter.Compute(walker, compilation, diagnostics, cancellationToken)
            : new List<ReachabilityExport>();

        return new GenerationModel(
            HasAbstractions: true,
            HasRegistrationType: compilation.GetTypeByMetadataName(RegistrationTypeName) is not null,
            Entries: entries.ToEquatableArray(),
            Errors: errors.ToEquatableArray(),
            Endpoints: endpoints.ToEquatableArray(),
            Diagnostics: diagnostics.ToEquatableArray(),
            Reachability: reachability.ToEquatableArray(),
            AssemblyName: compilation.AssemblyName ?? string.Empty);
    }

    /// <summary>
    /// Resolves what the exception pipeline can answer with beyond the catalog: the
    /// <c>MapUnhandledException(...)</c> fallback and the global handlers of every
    /// <c>HandleExceptions(h =&gt; ...)</c> block. Both are reachable from every endpoint by definition,
    /// so the codes come back as one set the caller puts on every contract. A fallback argument that
    /// is not a catalog read — a value built at runtime — answers on the wire but cannot be documented,
    /// which is <c>EAPI014</c>.
    /// </summary>
    private static SortedSet<string> ResolveGlobalErrors(
        Compilation compilation,
        ImmutableArray<InvocationExpressionSyntax> fallbackCalls,
        ImmutableArray<InvocationExpressionSyntax> handlingCalls,
        ErrorReachabilityWalker walker,
        List<DiagnosticInfo> diagnostics,
        System.Threading.CancellationToken cancellationToken)
    {
        var codes = new SortedSet<string>(System.StringComparer.Ordinal);

        if (compilation.GetTypeByMetadataName(ExceptionOptionsTypeName) is not { } exceptionOptionsType)
        {
            // ErrorApi.AspNetCore is not referenced: there is no exception pipeline to configure.
            return codes;
        }

        var builderType = compilation.GetTypeByMetadataName(HandlingBuilderTypeName);
        var optionsType = compilation.GetTypeByMetadataName(OptionsTypeName);

        foreach (var call in handlingCalls)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (call.ArgumentList.Arguments.Count != 1
                || call.ArgumentList.Arguments[0].Expression is not AnonymousFunctionExpressionSyntax block)
            {
                continue;
            }

            var model = compilation.GetSemanticModel(call.SyntaxTree);
            if (IsSomeoneElses(model, call, optionsType, cancellationToken))
            {
                continue;
            }

            // Every h.Add(...) inside the block. Its receiver is the block's lambda parameter, typeless
            // while the generator runs (see below), so the calls are matched by shape: a handler type
            // argument, or a lambda.
            foreach (var add in block.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (add.Expression is not MemberAccessExpressionSyntax { Name: var name }
                    || name.Identifier.ValueText != AddHandlerMethodName
                    || IsSomeoneElses(model, add, builderType, cancellationToken))
                {
                    continue;
                }

                if (name is GenericNameSyntax { TypeArgumentList.Arguments: { Count: 1 or 2 } typeArguments })
                {
                    // Add<THandler>() or Add<TException, THandler>(): the handler is the last argument,
                    // and every Map it declares is walked like an endpoint handler.
                    if (model.GetTypeInfo(typeArguments[typeArguments.Count - 1], cancellationToken).Type is INamedTypeSymbol handlerType)
                    {
                        foreach (var map in handlerType.GetMembers("Map").OfType<IMethodSymbol>())
                        {
                            codes.UnionWith(walker.CollectFromMethod(map).Codes);
                        }
                    }
                }
                else if (add.ArgumentList.Arguments.Count == 1
                         && add.ArgumentList.Arguments[0].Expression is AnonymousFunctionExpressionSyntax lambda)
                {
                    codes.UnionWith(walker.Collect(lambda, model).Codes);
                }
            }
        }

        foreach (var call in fallbackCalls)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var model = compilation.GetSemanticModel(call.SyntaxTree);
            if (IsSomeoneElses(model, call, exceptionOptionsType, cancellationToken)
                && IsSomeoneElses(model, call, builderType, cancellationToken))
            {
                continue;
            }

            switch (call.ArgumentList.Arguments.Count)
            {
                case 0:
                    // The built-in default: the same constants the runtime answers with, from one
                    // linked source — so the document and the wire agree by construction.
                    walker.Discovered.TryAdd(Shared.UnhandledDefaults.Code, new DiscoveredError(
                        Shared.UnhandledDefaults.Code,
                        Shared.UnhandledDefaults.StatusCode,
                        Shared.UnhandledDefaults.Title,
                        null,
                        Shared.UnhandledDefaults.Description,
                        Shared.UnhandledDefaults.DeclaringMember));
                    codes.Add(Shared.UnhandledDefaults.Code);
                    break;

                case 1:
                    var argument = call.ArgumentList.Arguments[0].Expression;
                    if (walker.TryResolveErrorCode(argument, model, out var code))
                    {
                        codes.Add(code!);
                    }
                    else
                    {
                        diagnostics.Add(DiagnosticInfo.Create(Diagnostics.UnresolvedFallback, argument));
                    }

                    break;
            }
        }

        return codes;
    }

    /// <summary>
    /// Whether an invocation is bound to a method of some type other than <paramref name="ours"/>.
    /// Bound to nothing at all is the expected case, not a failure: these calls sit inside
    /// <c>AddErrorApi(x =&gt; ...)</c>, an overload this generator itself emits, so while the generator
    /// runs the receiver has no type yet — and the arguments (catalog reads, type names, lambdas) bind
    /// on their own regardless.
    /// </summary>
    private static bool IsSomeoneElses(
        SemanticModel model, InvocationExpressionSyntax call, INamedTypeSymbol? ours, System.Threading.CancellationToken cancellationToken)
    {
        var info = model.GetSymbolInfo(call, cancellationToken);
        var bound = info.Symbol as IMethodSymbol ?? info.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();

        return bound is not null && !SymbolEqualityComparer.Default.Equals(bound.ContainingType, ours);
    }

    /// <summary>Puts the global codes on every endpoint: a fallback or a global handler is reachable from everywhere by definition.</summary>
    private static IReadOnlyList<EndpointModel> AppendToEvery(IReadOnlyList<EndpointModel> endpoints, SortedSet<string> codes) =>
        endpoints
            .Select(endpoint =>
            {
                var union = new SortedSet<string>(endpoint.ErrorCodes, System.StringComparer.Ordinal);
                union.UnionWith(codes);
                return endpoint with { ErrorCodes = new EquatableArray<string>(union.ToImmutableArray()) };
            })
            .ToList();

    /// <summary>
    /// Whether this compilation exports its reachability. On by default for a compilation with no
    /// endpoints — that is a library, and its whole point of running the generator is to be consumed.
    /// <c>&lt;ErrorApiExportReachability&gt;</c> in the project file overrides in either direction, and
    /// so does <c>errorapi_export_reachability</c> in .editorconfig; the project file wins.
    /// </summary>
    private static bool ExportsReachability(AnalyzerConfigOptionsProvider configuration, Compilation compilation, bool hasEndpoints)
    {
        if (configuration.GlobalOptions.TryGetValue("build_property.ErrorApiExportReachability", out var property)
            && bool.TryParse(property, out var fromProject))
        {
            return fromProject;
        }

        var tree = compilation.SyntaxTrees.FirstOrDefault();
        if (tree is not null
            && configuration.GetOptions(tree).TryGetValue("errorapi_export_reachability", out var value)
            && bool.TryParse(value, out var declared))
        {
            return declared;
        }

        return !hasEndpoints;
    }

    /// <summary>
    /// Which referenced assemblies the walk may read exports and catalogs from —
    /// <c>&lt;ErrorApiIncludeAssemblies&gt;MyProject.Domain;MyProject.*&lt;/ErrorApiIncludeAssemblies&gt;</c>
    /// in the project file. Unset means every reference, which is the default a layered application
    /// rarely needs to change; the property exists so the API project can say explicitly which layers
    /// it trusts the contract to come from.
    /// </summary>
    private static IReadOnlyList<string>? IncludeAssemblies(AnalyzerConfigOptionsProvider configuration)
    {
        if (!configuration.GlobalOptions.TryGetValue("build_property.ErrorApiIncludeAssemblies", out var value)
            || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var patterns = value
            .Split(new[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();

        return patterns.Count == 0 ? null : patterns;
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
                model.Reachability.AsImmutableArray(),
                model.AssemblyName));

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
