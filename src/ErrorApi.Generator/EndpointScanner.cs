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
    private const string EndpointRouteBuilderInterface = "Microsoft.AspNetCore.Routing.IEndpointRouteBuilder";
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
        List<DiagnosticInfo> diagnostics)
    {
        var walker = new ErrorReachabilityWalker(compilation) { MappedTypes = mappedTypes };
        var models = new Dictionary<(string Method, string Route), EndpointModel>();

        foreach (var invocation in candidates)
        {
            if (!compilation.ContainsSyntaxTree(invocation.SyntaxTree))
            {
                continue;
            }

            var model = compilation.GetSemanticModel(invocation.SyntaxTree);
            if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol symbol
                || !IsEndpointMapping(symbol)
                || !MethodByName.TryGetValue(symbol.Name, out var httpMethod))
            {
                continue;
            }

            var arguments = invocation.ArgumentList.Arguments;
            if (arguments.Count < 2)
            {
                continue;
            }

            var declaredPattern = model.GetConstantValue(arguments[0].Expression);
            if (!declaredPattern.HasValue || declaredPattern.Value is not string patternText)
            {
                diagnostics.Add(DiagnosticInfo.Create(Diagnostics.NonLiteralRoute, arguments[0].Expression));
                continue;
            }

            if (symbol.Name == "MapMethods")
            {
                httpMethod = ReadHttpMethods(arguments[1].Expression, model);
            }

            var prefix = ResolvePrefix(GetReceiver(invocation), model, 0);
            var route = RouteNormalizer.Combine(prefix, patternText);
            var handler = arguments[arguments.Count - 1].Expression;

            if (!ErrorReachabilityWalker.IsResolvable(handler, model))
            {
                diagnostics.Add(DiagnosticInfo.Create(Diagnostics.UnresolvedHandler, invocation, httpMethod, route));
            }

            walker.FollowDispatch = FollowsDispatch(configuration, invocation.SyntaxTree);

            var codes = walker.Collect(handler, model);
            var unresolved = walker.UnresolvedDispatches.FirstOrDefault();
            var key = (httpMethod, route);

            if (models.TryGetValue(key, out var existing))
            {
                // The same route mapped twice (for example once per feature module): union the contracts.
                codes.UnionWith(existing.ErrorCodes);
            }
            else if (codes.Count == 0 && unresolved is not null)
            {
                // More specific than EAPI006, and the one worth reporting: the walk did not come up
                // empty, it was stopped.
                diagnostics.Add(DiagnosticInfo.Create(Diagnostics.UnresolvedDispatch, invocation, httpMethod, route, unresolved));
            }
            else if (codes.Count == 0 && ReturnsResult(handler, model))
            {
                diagnostics.Add(DiagnosticInfo.Create(Diagnostics.NoErrorsDiscovered, invocation, httpMethod, route));
            }

            models[key] = new EndpointModel(
                HttpMethod: httpMethod,
                RoutePattern: route,
                DeclaredPattern: patternText,
                HandlerDisplay: DescribeHandler(handler, model),
                ErrorCodes: new EquatableArray<string>(codes.ToImmutableArray()),
                Location: LocationInfo.From(invocation));
        }

        var endpoints = models.Values
            .OrderBy(e => e.RoutePattern, System.StringComparer.Ordinal)
            .ThenBy(e => e.HttpMethod, System.StringComparer.Ordinal)
            .ToList();

        var discovered = walker.Discovered.Values
            .OrderBy(e => e.Code, System.StringComparer.Ordinal)
            .ToList();

        return new ScanResult(endpoints, discovered);
    }

    /// <summary>
    /// Reads <c>errorapi_follow_dispatch</c> from .editorconfig. A heuristic without an off switch is a
    /// liability; following a message into its handler is on unless a project says otherwise.
    /// </summary>
    private static bool FollowsDispatch(AnalyzerConfigOptionsProvider configuration, SyntaxTree tree) =>
        !configuration.GetOptions(tree).TryGetValue("errorapi_follow_dispatch", out var value)
        || !bool.TryParse(value, out var enabled)
        || enabled;

    private static bool IsEndpointMapping(IMethodSymbol symbol)
    {
        if (!symbol.IsExtensionMethod)
        {
            return false;
        }

        var receiver = symbol.ReceiverType ?? symbol.ReducedFrom?.Parameters.FirstOrDefault()?.Type;
        return receiver is not null && ImplementsEndpointRouteBuilder(receiver);
    }

    private static bool ImplementsEndpointRouteBuilder(ITypeSymbol type) =>
        type.ToDisplayString() == EndpointRouteBuilderInterface
        || type.AllInterfaces.Any(i => i.ToDisplayString() == EndpointRouteBuilderInterface);

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
        if ((info.Symbol ?? info.CandidateSymbols.FirstOrDefault()) is not IMethodSymbol method)
        {
            return false;
        }

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
