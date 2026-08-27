using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using ErrorApi.Generator.Helpers;
using ErrorApi.Generator.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ErrorApi.Generator;

/// <summary>
/// Turns attribute-routed controller actions into the same <see cref="ScannedEndpoint"/> shape the
/// Minimal API scanner produces. A controller endpoint is found by type, not by call site: a
/// non-abstract class deriving from <c>ControllerBase</c> (or marked <c>[ApiController]</c>), whose
/// public actions carry <c>[HttpGet]</c>/<c>[HttpPost]</c>/… — the action method itself is the handler
/// the reachability walk starts from.
/// </summary>
internal static class ControllerScanner
{
    private static readonly Dictionary<string, string> VerbByAttribute = new(System.StringComparer.Ordinal)
    {
        ["HttpGetAttribute"] = "GET",
        ["HttpPostAttribute"] = "POST",
        ["HttpPutAttribute"] = "PUT",
        ["HttpDeleteAttribute"] = "DELETE",
        ["HttpPatchAttribute"] = "PATCH",
        ["HttpHeadAttribute"] = "HEAD",
        ["HttpOptionsAttribute"] = "OPTIONS",
        ["RouteAttribute"] = "*",
    };

    public static IReadOnlyList<ScannedEndpoint> Scan(
        Compilation compilation,
        AnalyzerConfigOptionsProvider configuration,
        ErrorReachabilityWalker walker,
        System.Threading.CancellationToken cancellationToken)
    {
        var controllers = FindControllers(compilation);
        if (controllers.Count == 0)
        {
            return [];
        }

        // Indexed results keep diagnostics and merge order deterministic across the parallel scan.
        var scanned = new List<ScannedEndpoint>[controllers.Count];

        System.Threading.Tasks.Parallel.For(
            0,
            controllers.Count,
            new System.Threading.Tasks.ParallelOptions { CancellationToken = cancellationToken },
            index => scanned[index] = ScanController(controllers[index], configuration, walker));

        return scanned.SelectMany(list => list).ToList();
    }

    private static List<INamedTypeSymbol> FindControllers(Compilation compilation)
    {
        var controllers = new List<INamedTypeSymbol>();
        var queue = new Queue<INamespaceOrTypeSymbol>();
        queue.Enqueue(compilation.Assembly.GlobalNamespace);

        while (queue.Count > 0)
        {
            foreach (var member in queue.Dequeue().GetMembers())
            {
                if (member is INamespaceSymbol nested)
                {
                    queue.Enqueue(nested);
                }
                else if (member is INamedTypeSymbol type)
                {
                    if (IsController(type))
                    {
                        controllers.Add(type);
                    }

                    queue.Enqueue(type);
                }
            }
        }

        controllers.Sort((a, b) => System.StringComparer.Ordinal.Compare(a.Name, b.Name));
        return controllers;
    }

    private static bool IsController(INamedTypeSymbol type) =>
        type is { TypeKind: TypeKind.Class, IsAbstract: false }
        && (DerivesFromControllerBase(type) || HasMvcAttribute(type, "ApiControllerAttribute"));

    private static bool DerivesFromControllerBase(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.Name == "ControllerBase" && IsMvcNamespace(current.ContainingNamespace))
            {
                return true;
            }
        }

        return false;
    }

    private static List<ScannedEndpoint> ScanController(
        INamedTypeSymbol controller,
        AnalyzerConfigOptionsProvider configuration,
        ErrorReachabilityWalker walker)
    {
        var results = new List<ScannedEndpoint>();
        var prefixes = RouteTemplates(controller, "RouteAttribute");
        if (prefixes.Count == 0)
        {
            prefixes.Add(null);
        }

        var controllerName = TrimSuffix(controller.Name, "Controller");
        var typeSuppressed = CatalogParser.SuppressedIds(controller);

        foreach (var action in controller.GetMembers().OfType<IMethodSymbol>())
        {
            if (action.MethodKind != MethodKind.Ordinary
                || action.IsStatic
                || action.DeclaredAccessibility != Accessibility.Public
                || HasMvcAttribute(action, "NonActionAttribute"))
            {
                continue;
            }

            var routes = ActionRoutes(action);
            if (routes.Count == 0)
            {
                // Conventional routing: the template lives in UseEndpoints configuration, not on the
                // action, so there is nothing compile-time to match the operation by.
                continue;
            }

            var reference = action.DeclaringSyntaxReferences.FirstOrDefault();
            if (reference is null)
            {
                continue;
            }

            var node = reference.GetSyntax();
            var options = configuration.GetOptions(node.SyntaxTree);
            var walk = walker.CollectFromMethod(
                action,
                EndpointScanner.FollowsDispatch(options),
                EndpointScanner.WalkDepth(options));

            var suppressed = typeSuppressed.Union(CatalogParser.SuppressedIds(action));
            var codes = new EquatableArray<string>(walk.Codes.ToImmutableArray());
            var display = action.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
            var location = LocationInfo.From(node);

            foreach (var (verb, template) in routes)
            {
                foreach (var prefix in Routes(prefixes, template))
                {
                    var replaced = ReplaceTokens(prefix, controllerName, action.Name);
                    if (replaced.IndexOf('[') >= 0)
                    {
                        // A token this scanner cannot resolve, e.g. [area]; better no entry than a
                        // wrong one, and the diagnostic says which endpoint went undocumented.
                        if (!suppressed.Contains(Diagnostics.NonLiteralRoute.Id))
                        {
                            results.Add(new ScannedEndpoint(
                                null, null, false, suppressed,
                                [DiagnosticInfo.Create(Diagnostics.NonLiteralRoute, location)]));
                        }

                        continue;
                    }

                    results.Add(new ScannedEndpoint(
                        new EndpointModel(
                            HttpMethod: verb,
                            RoutePattern: RouteNormalizer.Normalize(replaced),
                            DeclaredPattern: replaced,
                            HandlerDisplay: display,
                            ErrorCodes: codes,
                            Location: location),
                        walk.UnresolvedDispatches.FirstOrDefault(),
                        EndpointScanner.ReturnsResult(action),
                        suppressed,
                        []));
                }
            }
        }

        return results;
    }

    /// <summary>All (verb, template) pairs an action declares. One attribute, one route.</summary>
    private static List<(string Verb, string? Template)> ActionRoutes(IMethodSymbol action)
    {
        var routes = new List<(string, string?)>();

        foreach (var attribute in action.GetAttributes())
        {
            if (attribute.AttributeClass is not { } cls
                || !VerbByAttribute.TryGetValue(cls.Name, out var verb)
                || !IsMvcOrRoutingNamespace(cls.ContainingNamespace))
            {
                continue;
            }

            var template = attribute.ConstructorArguments.Length > 0
                ? attribute.ConstructorArguments[0].Value as string
                : null;

            routes.Add((verb, template));
        }

        return routes;
    }

    private static List<string?> RouteTemplates(INamedTypeSymbol type, string attributeName)
    {
        var templates = new List<string?>();

        foreach (var attribute in type.GetAttributes())
        {
            if (attribute.AttributeClass is { } cls
                && cls.Name == attributeName
                && IsMvcOrRoutingNamespace(cls.ContainingNamespace)
                && attribute.ConstructorArguments.Length > 0
                && attribute.ConstructorArguments[0].Value is string template)
            {
                templates.Add(template);
            }
        }

        return templates;
    }

    /// <summary>
    /// Combines the controller's route prefix with the action's template, honouring MVC's rule that a
    /// template rooted with <c>/</c> or <c>~/</c> replaces the prefix instead of appending to it.
    /// </summary>
    private static IEnumerable<string> Routes(List<string?> prefixes, string? template)
    {
        if (template is not null
            && (template.StartsWith("/", System.StringComparison.Ordinal)
                || template.StartsWith("~/", System.StringComparison.Ordinal)))
        {
            yield return template.TrimStart('~');
            yield break;
        }

        foreach (var prefix in prefixes)
        {
            if (prefix is null)
            {
                yield return template ?? string.Empty;
            }
            else if (string.IsNullOrEmpty(template))
            {
                yield return prefix;
            }
            else
            {
                yield return prefix.TrimEnd('/') + "/" + template!.TrimStart('/');
            }
        }
    }

    /// <summary>Replaces the <c>[controller]</c> and <c>[action]</c> tokens, case-insensitively.</summary>
    private static string ReplaceTokens(string template, string controller, string action)
    {
        var result = ReplaceToken(template, "[controller]", controller);
        return ReplaceToken(result, "[action]", action);
    }

    private static string ReplaceToken(string template, string token, string value)
    {
        while (true)
        {
            var index = template.IndexOf(token, System.StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return template;
            }

            template = template.Substring(0, index) + value + template.Substring(index + token.Length);
        }
    }

    private static bool HasMvcAttribute(ISymbol symbol, string attributeName) =>
        symbol.GetAttributes().Any(a =>
            a.AttributeClass is { } cls && cls.Name == attributeName && IsMvcNamespace(cls.ContainingNamespace));

    private static bool IsMvcNamespace(INamespaceSymbol? ns) =>
        ns is { Name: "Mvc", ContainingNamespace: { Name: "AspNetCore", ContainingNamespace: { Name: "Microsoft" } microsoft } }
        && microsoft.ContainingNamespace.IsGlobalNamespace;

    /// <summary>MVC's own attributes plus <c>Microsoft.AspNetCore.Mvc.Routing</c>, where HttpGet and friends live.</summary>
    private static bool IsMvcOrRoutingNamespace(INamespaceSymbol? ns) =>
        IsMvcNamespace(ns) || (ns is { Name: "Routing" } && IsMvcNamespace(ns.ContainingNamespace));

    private static string TrimSuffix(string name, string suffix) =>
        name.Length > suffix.Length && name.EndsWith(suffix, System.StringComparison.Ordinal)
            ? name.Substring(0, name.Length - suffix.Length)
            : name;
}
