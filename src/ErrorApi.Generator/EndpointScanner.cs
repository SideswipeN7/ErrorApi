using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using ErrorApi.Generator.Helpers;
using ErrorApi.Generator.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ErrorApi.Generator;

/// <summary>
/// Turns Minimal API <c>Map*</c> call sites into <see cref="EndpointModel"/> entries: HTTP method and
/// route template from the call itself, and the reachable error codes from
/// <see cref="ErrorReachabilityWalker"/>.
/// </summary>
internal static class EndpointScanner
{
    private const int MaxPrefixDepth = 8;

    private static readonly Dictionary<string, string> MethodByName = new()
    {
        ["MapGet"] = "GET",
        ["MapPost"] = "POST",
        ["MapPut"] = "PUT",
        ["MapDelete"] = "DELETE",
        ["MapPatch"] = "PATCH",
        ["MapMethods"] = "*",
        ["Map"] = "*",
    };

    /// <summary>The names worth a semantic lookup; the syntax predicate stays allocation-free.</summary>
    public static bool IsCandidateName(string name) => MethodByName.ContainsKey(name) || name == "MapGroup";

    public static ScanResult Scan(
        Compilation compilation,
        IReadOnlyList<InvocationExpressionSyntax> candidates,
        AnalyzerConfigOptionsProvider configuration,
        IReadOnlyDictionary<string, string> mappedTypes,
        List<DiagnosticInfo> diagnostics,
        IReadOnlyList<string>? includeAssemblies = null,
        System.Threading.CancellationToken cancellationToken = default)
    {
        var walker = new ErrorReachabilityWalker(compilation)
        {
            MappedTypes = mappedTypes,
            ForeignAssemblyFilter = includeAssemblies,
        };
        var models = new Dictionary<(string Method, string Route, string? Group), EndpointModel>();

        // Binding an endpoint statement — the Map* overload resolution plus the handler body — is where
        // this generator's time goes, and endpoints are independent of each other: semantic models are
        // thread-safe and the walker keeps only concurrent or per-Collect state. The results land in an
        // indexed array and are merged sequentially below, so diagnostics and route unions come out in
        // the same order a sequential scan produced.
        var scanned = new ScannedEndpoint?[candidates.Count];

        System.Threading.Tasks.Parallel.For(
            0,
            candidates.Count,
            new System.Threading.Tasks.ParallelOptions { CancellationToken = cancellationToken },
            index => scanned[index] = ScanCandidate(compilation, candidates[index], configuration, walker));

        // Controllers are the second endpoint surface: attribute-routed actions found by type, not by
        // call site. They produce the same ScannedEndpoint shape and go through the same merge, so an
        // application can mix Minimal APIs and controllers in one document.
        var controllerScanned = ControllerScanner.Scan(compilation, configuration, walker, cancellationToken);

        foreach (var item in scanned.Concat(controllerScanned))
        {
            if (item is null)
            {
                continue;
            }

            item.Diagnostics.ForEach(diagnostics.Add);

            if (item.Endpoint is not { } endpoint)
            {
                continue;
            }

            var codes = new SortedSet<string>(endpoint.ErrorCodes, System.StringComparer.Ordinal);
            var key = (endpoint.HttpMethod, endpoint.RoutePattern, endpoint.Group);

            if (models.TryGetValue(key, out var existing))
            {
                // The same route mapped twice (for example once per feature module): union the contracts.
                codes.UnionWith(existing.ErrorCodes);
            }
            else if (item.UnresolvedDispatch is not null)
            {
                // A stopped walk is worth reporting whether or not something else was found first: a
                // partial contract reads as complete, which is the worst way for it to be wrong.
                if (!item.Suppressed.Contains(Diagnostics.UnresolvedDispatch.Id))
                {
                    diagnostics.Add(DiagnosticInfo.Create(
                        Diagnostics.UnresolvedDispatch, endpoint.Location, endpoint.HttpMethod, endpoint.RoutePattern, item.UnresolvedDispatch));
                }
            }
            else if (codes.Count == 0 && item.HandlerReturnsResult && !item.Suppressed.Contains(Diagnostics.NoErrorsDiscovered.Id))
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    Diagnostics.NoErrorsDiscovered, endpoint.Location, endpoint.HttpMethod, endpoint.RoutePattern));
            }

            models[key] = endpoint with { ErrorCodes = new EquatableArray<string>(codes.ToImmutableArray()) };
        }

        var endpoints = models.Values
            .OrderBy(e => e.RoutePattern, System.StringComparer.Ordinal)
            .ThenBy(e => e.HttpMethod, System.StringComparer.Ordinal)
            .ThenBy(e => e.Group, System.StringComparer.Ordinal)
            .ToList();

        var discovered = walker.Discovered.Values
            .OrderBy(e => e.Code, System.StringComparer.Ordinal)
            .ToList();

        return new ScanResult(endpoints, discovered);
    }


    /// <summary>Reads one candidate invocation. Runs concurrently with the others; touches no shared state.</summary>
    private static ScannedEndpoint? ScanCandidate(
        Compilation compilation,
        InvocationExpressionSyntax invocation,
        AnalyzerConfigOptionsProvider configuration,
        ErrorReachabilityWalker walker)
    {
        if (!compilation.ContainsSyntaxTree(invocation.SyntaxTree))
        {
            return null;
        }

        // The name and the receiver's type identify the call. Resolving the full invocation would mean
        // overload resolution against the Minimal API Delegate overloads for every endpoint.
        if (invocation.Expression is not MemberAccessExpressionSyntax member
            || !MethodByName.TryGetValue(member.Name.Identifier.ValueText, out var httpMethod))
        {
            return null;
        }

        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count < 2)
        {
            return null;
        }

        var model = compilation.GetSemanticModel(invocation.SyntaxTree);
        if (model.GetTypeInfo(member.Expression).Type is not { } receiverType
            || !ImplementsEndpointRouteBuilder(receiverType))
        {
            return null;
        }

        var diagnostics = new List<DiagnosticInfo>();

        // [SuppressErrorApi] on the mapping method or on the handler: generator diagnostics ignore
        // #pragma, so this is the per-endpoint lever.
        var enclosing = invocation.Ancestors().OfType<BaseMethodDeclarationSyntax>().FirstOrDefault();
        var suppressed = CatalogParser.SuppressedIds(enclosing is null ? null : model.GetDeclaredSymbol(enclosing));

        var declaredPattern = model.GetConstantValue(arguments[0].Expression);
        if (!declaredPattern.HasValue || declaredPattern.Value is not string patternText)
        {
            if (!suppressed.Contains(Diagnostics.NonLiteralRoute.Id))
            {
                diagnostics.Add(DiagnosticInfo.Create(Diagnostics.NonLiteralRoute, arguments[0].Expression));
            }

            return new ScannedEndpoint(null, null, false, suppressed, diagnostics);
        }

        if (member.Name.Identifier.ValueText == "MapMethods")
        {
            httpMethod = ReadHttpMethods(arguments[1].Expression, model);
        }

        var prefix = ResolvePrefix(GetReceiver(invocation), model, 0);
        var route = RouteNormalizer.Combine(prefix, patternText);
        var handler = arguments[arguments.Count - 1].Expression;

        var handlerInfo = model.GetSymbolInfo(handler);
        suppressed = suppressed.Union(
            CatalogParser.SuppressedIds(handlerInfo.Symbol ?? handlerInfo.CandidateSymbols.FirstOrDefault()));

        if (!ErrorReachabilityWalker.IsResolvable(handler, model) && !suppressed.Contains(Diagnostics.UnresolvedHandler.Id))
        {
            diagnostics.Add(DiagnosticInfo.Create(Diagnostics.UnresolvedHandler, invocation, httpMethod, route));
        }

        var options = configuration.GetOptions(invocation.SyntaxTree);
        var walk = walker.Collect(handler, model, FollowsDispatch(options), WalkDepth(options));

        var endpoint = new EndpointModel(
            HttpMethod: httpMethod,
            RoutePattern: route,
            DeclaredPattern: patternText,
            HandlerDisplay: DescribeHandler(handler, model),
            ErrorCodes: new EquatableArray<string>(walk.Codes.ToImmutableArray()),
            Location: LocationInfo.From(invocation),
            Group: ResolveGroup(invocation, model));

        return new ScannedEndpoint(
            endpoint,
            walk.UnresolvedDispatches.FirstOrDefault(),
            ReturnsResult(handler, model),
            suppressed,
            diagnostics);
    }

    /// <summary>
    /// Reads <c>errorapi_follow_dispatch</c> from .editorconfig. A heuristic without an off switch is a
    /// liability; following a message into its handler is on unless a project says otherwise.
    /// </summary>
    internal static bool FollowsDispatch(AnalyzerConfigOptions options) =>
        !options.TryGetValue("errorapi_follow_dispatch", out var value)
        || !bool.TryParse(value, out var enabled)
        || enabled;

    /// <summary>
    /// Reads <c>errorapi_walk_depth</c> from .editorconfig. Twelve is generous for an endpoint, but an
    /// unusually layered application should be able to say so instead of losing its deepest failures.
    /// </summary>
    internal static int WalkDepth(AnalyzerConfigOptions options) =>
        options.TryGetValue("errorapi_walk_depth", out var value)
        && int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var depth)
        && depth > 0
            ? depth
            : ErrorReachabilityWalker.DefaultMaxDepth;

    private static bool ImplementsEndpointRouteBuilder(ITypeSymbol type) =>
        IsEndpointRouteBuilder(type) || type.AllInterfaces.Any(IsEndpointRouteBuilder);

    private static bool IsEndpointRouteBuilder(ITypeSymbol type) =>
        type is INamedTypeSymbol { Name: "IEndpointRouteBuilder", ContainingNamespace: { Name: "Routing" } routing }
        && routing.ContainingNamespace is { Name: "AspNetCore" } aspnet
        && aspnet.ContainingNamespace is { Name: "Microsoft" } microsoft
        && microsoft.ContainingNamespace.IsGlobalNamespace;

    private static string ReadHttpMethods(ExpressionSyntax expression, SemanticModel model)
    {
        var literals = expression
            .DescendantNodesAndSelf()
            .OfType<LiteralExpressionSyntax>()
            .Select(l => model.GetConstantValue(l).Value as string)
            .Where(v => !string.IsNullOrEmpty(v))
            .Select(v => v!.ToUpperInvariant())
            .Distinct(System.StringComparer.Ordinal)
            .OrderBy(v => v, System.StringComparer.Ordinal)
            .ToList();

        return literals.Count == 0 ? "*" : string.Join(",", literals);
    }

    private static ExpressionSyntax? GetReceiver(InvocationExpressionSyntax invocation) =>
        invocation.Expression is MemberAccessExpressionSyntax member ? member.Expression : null;

    /// <summary>
    /// Resolves the endpoint's API description group: <c>WithGroupName(...)</c> chained after the
    /// <c>Map*</c> call wins, then one inherited from the builder chain (a group's, or one on the local
    /// the builder was assigned to). This is what tells two versions of the same route apart.
    /// </summary>
    private static string? ResolveGroup(InvocationExpressionSyntax invocation, SemanticModel model) =>
        TrailingGroup(invocation, model) ?? ReceiverGroup(GetReceiver(invocation), model, 0);

    private static string? TrailingGroup(InvocationExpressionSyntax invocation, SemanticModel model)
    {
        SyntaxNode current = invocation;

        while (current.Parent is MemberAccessExpressionSyntax { Parent: InvocationExpressionSyntax outer } member
               && member.Expression == current)
        {
            if (member.Name.Identifier.ValueText == "WithGroupName"
                && outer.ArgumentList.Arguments.Count > 0
                && model.GetConstantValue(outer.ArgumentList.Arguments[0].Expression).Value is string name)
            {
                return name;
            }

            current = outer;
        }

        return null;
    }

    /// <summary>The receiver-chain twin of <see cref="ResolvePrefix"/>: nearest declaration wins.</summary>
    private static string? ReceiverGroup(ExpressionSyntax? receiver, SemanticModel model, int depth)
    {
        if (receiver is null || depth > MaxPrefixDepth)
        {
            return null;
        }

        switch (receiver)
        {
            case InvocationExpressionSyntax invocation:
            {
                if (invocation.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "WithGroupName" }
                    && invocation.ArgumentList.Arguments.Count > 0
                    && model.GetConstantValue(invocation.ArgumentList.Arguments[0].Expression).Value is string name)
                {
                    return name;
                }

                return ReceiverGroup(GetReceiver(invocation), model, depth + 1);
            }

            case ParenthesizedExpressionSyntax parenthesized:
                return ReceiverGroup(parenthesized.Expression, model, depth + 1);

            case IdentifierNameSyntax or MemberAccessExpressionSyntax:
            {
                var symbol = model.GetSymbolInfo(receiver).Symbol;
                if (symbol is null)
                {
                    return null;
                }

                foreach (var reference in symbol.DeclaringSyntaxReferences)
                {
                    var initializer = reference.GetSyntax() switch
                    {
                        VariableDeclaratorSyntax declarator => declarator.Initializer?.Value,
                        PropertyDeclarationSyntax property => property.Initializer?.Value ?? property.ExpressionBody?.Expression,
                        _ => null,
                    };

                    if (initializer is null)
                    {
                        continue;
                    }

                    var declaringModel = model.SyntaxTree == initializer.SyntaxTree
                        ? model
                        : model.Compilation.ContainsSyntaxTree(initializer.SyntaxTree)
                            ? model.Compilation.GetSemanticModel(initializer.SyntaxTree)
                            : null;

                    if (declaringModel is not null)
                    {
                        return ReceiverGroup(initializer, declaringModel, depth + 1);
                    }
                }

                return null;
            }

            default:
                return null;
        }
    }

    /// <summary>
    /// Rebuilds the route prefix contributed by <c>MapGroup</c>, following the builder back through
    /// intermediate calls and through the local it was assigned to.
    /// </summary>
    private static string ResolvePrefix(ExpressionSyntax? receiver, SemanticModel model, int depth)
    {
        if (receiver is null || depth > MaxPrefixDepth)
        {
            return string.Empty;
        }

        switch (receiver)
        {
            case InvocationExpressionSyntax invocation:
            {
                var inner = ResolvePrefix(GetReceiver(invocation), model, depth + 1);
                if (invocation.Expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "MapGroup" }
                    || invocation.ArgumentList.Arguments.Count == 0)
                {
                    return inner;
                }

                var constant = model.GetConstantValue(invocation.ArgumentList.Arguments[0].Expression);
                return constant.Value is string prefix ? Join(inner, prefix) : inner;
            }

            case ParenthesizedExpressionSyntax parenthesized:
                return ResolvePrefix(parenthesized.Expression, model, depth + 1);

            case IdentifierNameSyntax or MemberAccessExpressionSyntax:
            {
                var symbol = model.GetSymbolInfo(receiver).Symbol;
                if (symbol is null)
                {
                    return string.Empty;
                }

                foreach (var reference in symbol.DeclaringSyntaxReferences)
                {
                    var initializer = reference.GetSyntax() switch
                    {
                        VariableDeclaratorSyntax declarator => declarator.Initializer?.Value,
                        PropertyDeclarationSyntax property => property.Initializer?.Value ?? property.ExpressionBody?.Expression,
                        _ => null,
                    };

                    if (initializer is null)
                    {
                        continue;
                    }

                    var declaringModel = model.SyntaxTree == initializer.SyntaxTree
                        ? model
                        : model.Compilation.ContainsSyntaxTree(initializer.SyntaxTree)
                            ? model.Compilation.GetSemanticModel(initializer.SyntaxTree)
                            : null;

                    if (declaringModel is not null)
                    {
                        return ResolvePrefix(initializer, declaringModel, depth + 1);
                    }
                }

                return string.Empty;
            }

            default:
                return string.Empty;
        }
    }

    private static string Join(string outer, string inner) =>
        outer.Length == 0 ? inner : outer.TrimEnd('/') + "/" + inner.TrimStart('/');

    /// <summary>
    /// True when the handler hands back a <c>Result</c>, or a <c>TypedResults</c> union with
    /// <c>ProblemHttpResult</c> in it. Both shapes promise a failure path, so such an endpoint is
    /// expected to reach at least one catalog entry and finding none is worth reporting; plain
    /// endpoints stay quiet.
    /// </summary>
    private static bool ReturnsResult(ExpressionSyntax handler, SemanticModel model)
    {
        var info = model.GetSymbolInfo(handler);
        return (info.Symbol ?? info.CandidateSymbols.FirstOrDefault()) is IMethodSymbol method
            && ReturnsResult(method);
    }

    internal static bool ReturnsResult(IMethodSymbol method)
    {
        var returnType = method.ReturnType;
        if (returnType is INamedTypeSymbol { IsGenericType: true } named
            && named.ConstructedFrom.ToDisplayString() is "System.Threading.Tasks.Task<TResult>" or "System.Threading.Tasks.ValueTask<TResult>")
        {
            returnType = named.TypeArguments[0];
        }

        var name = returnType.OriginalDefinition.ToDisplayString();
        if (name is "ErrorApi.Result" or "ErrorApi.Result<T>")
        {
            return true;
        }

        // Results<Ok<T>, ProblemHttpResult> and friends: the union names a problem arm explicitly.
        return returnType is INamedTypeSymbol { Name: "Results", IsGenericType: true } union
            && union.ContainingNamespace.ToDisplayString() == "Microsoft.AspNetCore.Http.HttpResults"
            && union.TypeArguments.Any(t => t.Name == "ProblemHttpResult");
    }

    private static string DescribeHandler(ExpressionSyntax handler, SemanticModel model)
    {
        if (handler is AnonymousFunctionExpressionSyntax)
        {
            return "<lambda>";
        }

        var info = model.GetSymbolInfo(handler);
        var symbol = info.Symbol ?? info.CandidateSymbols.FirstOrDefault();
        return symbol?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) ?? handler.ToString();
    }
}

/// <summary>Everything one scan of the compilation produced.</summary>
internal sealed record ScanResult(IReadOnlyList<EndpointModel> Endpoints, IReadOnlyList<DiscoveredError> DiscoveredErrors);
