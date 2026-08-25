using System;
using System.Collections.Generic;
using System.Linq;
using ErrorApi.Generator.Helpers;
using ErrorApi.Generator.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ErrorApi.Generator;

/// <summary>
/// Walks outward from an endpoint handler and collects every catalog entry the handler can reach.
/// Calls are followed through source bodies and through local functions, and also through interface
/// and virtual dispatch to the implementations present in the compilation, which is what makes the
/// result useful in a layered application.
/// </summary>
internal sealed class ErrorReachabilityWalker
{
    private const int MaxDepth = 12;

    private readonly Compilation _compilation;

    // One walker serves every endpoint, and the scanner walks endpoints in parallel — everything an
    // individual Collect can touch is either concurrent or per-call state.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<SyntaxTree, SemanticModel> _models = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<INamedTypeSymbol, List<INamedTypeSymbol>> _implementations =
        new(SymbolEqualityComparer.Default);

    private readonly System.Collections.Concurrent.ConcurrentDictionary<INamedTypeSymbol, List<INamedTypeSymbol>> _handlers =
        new(SymbolEqualityComparer.Default);

    private readonly System.Collections.Concurrent.ConcurrentDictionary<IAssemblySymbol, List<INamedTypeSymbol>> _pipelines =
        new(SymbolEqualityComparer.Default);

    private readonly Lazy<List<INamedTypeSymbol>> _sourceTypes;

    public ErrorReachabilityWalker(Compilation compilation)
    {
        _compilation = compilation;
        _sourceTypes = new Lazy<List<INamedTypeSymbol>>(CollectSourceTypes, isThreadSafe: true);
    }

    /// <summary>
    /// Wire codes for types declared through <c>[assembly: ErrorMapping]</c>, keyed by fully qualified
    /// name. A type from a referenced package carries no attribute of ours, so this is the only way the
    /// walk can recognise one your code constructs.
    /// </summary>
    public IReadOnlyDictionary<string, string> MappedTypes { get; set; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Every <c>[Error]</c> declaration met while walking, keyed by code. Entries coming from a
    /// referenced assembly land here too, so an app can document a catalog it does not declare itself.
    /// </summary>
    public System.Collections.Concurrent.ConcurrentDictionary<string, DiscoveredError> Discovered { get; } =
        new(StringComparer.Ordinal);

    /// <summary>Collects the error codes reachable from a handler expression: a lambda or a method group.</summary>
    /// <param name="handlerNode">The handler expression: a lambda or a method group.</param>
    /// <param name="model">The semantic model of the tree the handler lives in.</param>
    /// <param name="followDispatch">
    /// Whether to follow a message past a dispatcher it cannot read, such as a mediator's <c>Send</c>.
    /// </param>
    public CollectResult Collect(SyntaxNode handlerNode, SemanticModel model, bool followDispatch = true)
    {
        var walk = new Walk(
            new SortedSet<string>(StringComparer.Ordinal),
            new HashSet<ISymbol>(SymbolEqualityComparer.Default),
            new List<string>(),
            followDispatch);

        if (handlerNode is AnonymousFunctionExpressionSyntax lambda)
        {
            if (model.GetSymbolInfo(lambda).Symbol is IMethodSymbol lambdaSymbol)
            {
                AddDeclaredCodes(lambdaSymbol, walk.Codes);
            }

            VisitNode(lambda, 0, walk);
            return new CollectResult(walk.Codes, walk.UnresolvedDispatches);
        }

        var info = model.GetSymbolInfo(handlerNode);
        var symbol = info.Symbol ?? info.CandidateSymbols.FirstOrDefault();
        if (symbol is IMethodSymbol method)
        {
            VisitMethod(method, 0, walk);
        }

        return new CollectResult(walk.Codes, walk.UnresolvedDispatches);
    }

    /// <summary>What one endpoint's walk found, and where it was stopped.</summary>
    /// <param name="Codes">The catalog codes reachable from the handler.</param>
    /// <param name="UnresolvedDispatches">
    /// Calls this walk could not see past. A mediator, a message bus or any other indirection whose
    /// implementation lives outside the compilation ends the walk, and an endpoint behind one is
    /// documented as having no failures at all — which is worse than being wrong, because it looks
    /// deliberate. <c>EndpointScanner</c> turns these into <c>EAPI009</c>.
    /// </param>
    public sealed record CollectResult(SortedSet<string> Codes, List<string> UnresolvedDispatches);

    /// <summary>The per-Collect state one walk threads through its visits.</summary>
    private sealed record Walk(
        SortedSet<string> Codes,
        HashSet<ISymbol> Visited,
        List<string> UnresolvedDispatches,
        bool FollowDispatch);

    /// <summary>True when the handler expression resolves to something the walker can actually read.</summary>
    public static bool IsResolvable(SyntaxNode handlerNode, SemanticModel model)
    {
        if (handlerNode is AnonymousFunctionExpressionSyntax)
        {
            return true;
        }

        var info = model.GetSymbolInfo(handlerNode);
        var symbol = info.Symbol ?? info.CandidateSymbols.FirstOrDefault();
        return symbol is IMethodSymbol { DeclaringSyntaxReferences.Length: > 0 };
    }

    private void VisitMethod(IMethodSymbol method, int depth, Walk walk)
    {
        if (depth > MaxDepth)
        {
            return;
        }

        method = method.OriginalDefinition;
        if (!walk.Visited.Add(method))
        {
            return;
        }

        AddDeclaredCodes(method, walk.Codes);

        if (method.ContainingType is { } containingType)
        {
            AddDeclaredCodes(containingType, walk.Codes);

            if (containingType.TypeKind == TypeKind.Interface || method.IsAbstract || method.IsVirtual)
            {
                foreach (var implementation in FindImplementations(method))
                {
                    VisitMethod(implementation, depth, walk);
                }
            }
        }

        foreach (var reference in method.DeclaringSyntaxReferences)
        {
            VisitNode(reference.GetSyntax(), depth, walk);
        }
    }

    private void VisitNode(SyntaxNode root, int depth, Walk walk)
    {
        if (depth > MaxDepth || !_compilation.ContainsSyntaxTree(root.SyntaxTree))
        {
            return;
        }

        var model = GetModel(root.SyntaxTree);

        foreach (var node in root.DescendantNodes())
        {
            if (node is InvocationExpressionSyntax invocation)
            {
                if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol target)
                {
                    continue;
                }

                if (TryGetErrorCode(target, out var invokedCode))
                {
                    walk.Codes.Add(invokedCode!);
                }
                else
                {
                    VisitMethod(target, depth + 1, walk);
                    VisitDispatchTargets(invocation, target, model, depth, walk);
                }
            }
            else if (node is BaseObjectCreationExpressionSyntax creation)
            {
                // `new OrderNotFound()` where the type carries [Error]: the type is the failure.
                var created = model.GetSymbolInfo(creation).Symbol is IMethodSymbol constructor
                    ? constructor.ContainingType
                    : model.GetTypeInfo(creation).Type;

                if (created is not null && (TryGetErrorCode(created, out var typeCode) || TryGetMappedCode(created, out typeCode)))
                {
                    walk.Codes.Add(typeCode!);
                }
            }
            else if (node is IdentifierNameSyntax or MemberAccessExpressionSyntax)
            {
                var referenced = model.GetSymbolInfo(node).Symbol;
                if (referenced is IPropertySymbol or IFieldSymbol && TryGetErrorCode(referenced, out var memberCode))
                {
                    walk.Codes.Add(memberCode!);
                }
            }
        }
    }

    /// <summary>
    /// Follows a message past a dispatcher the walk cannot read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>sender.Send(new GetOrder(id))</c> ends the walk: <c>ISender.Send</c> is implemented in a
    /// referenced assembly, so there is nothing to step into — and the handler that actually raises the
    /// failures is right there in the compilation, just not reachable by following calls.
    /// </para>
    /// <para>
    /// The bridge is the message type. A handler is a source type that implements a generic interface
    /// constructed with the message, which is the shape MediatR, Wolverine and Brighter all share, so
    /// nothing here names a library. The argument type has to come from source: a dispatcher is handed
    /// your own message types, and requiring that keeps <c>IEquatable&lt;Guid&gt;</c> and friends out.
    /// </para>
    /// <para>
    /// This deliberately over-matches a little. A validator declared as <c>IValidator&lt;GetOrder&gt;</c>
    /// matches too, and walking it is right: its failures do reach that endpoint.
    /// </para>
    /// </remarks>
    private void VisitDispatchTargets(
        InvocationExpressionSyntax invocation,
        IMethodSymbol target,
        SemanticModel model,
        int depth,
        Walk walk)
    {
        // Only a call the walk could not follow is worth reinterpreting as a dispatch. The question is
        // not whether the callee has source — an interface declared right here has plenty — but whether
        // there is an implementation to step into.
        if (!walk.FollowDispatch || depth > MaxDepth || !IsUnresolvedDispatchShape(target))
        {
            return;
        }

        var sawMessage = false;

        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (model.GetTypeInfo(argument.Expression).Type is not INamedTypeSymbol { DeclaringSyntaxReferences.Length: > 0 } message)
            {
                continue;
            }

            sawMessage = true;

            // A cross-cutting failure has no better home than the message that rides through it, so the
            // message type's own [ProducesError] counts as reached the moment it is dispatched.
            AddDeclaredCodes(message, walk.Codes);

            var handlers = FindHandlersFor(message);
            if (handlers.Count == 0)
            {
                walk.UnresolvedDispatches.Add(target.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat));
                continue;
            }

            foreach (var handler in handlers)
            {
                foreach (var method in handler.GetMembers().OfType<IMethodSymbol>())
                {
                    VisitMethod(method, depth + 1, walk);
                }
            }
        }

        // Every message through this dispatcher also rides its pipeline: source types generic over the
        // request, closed only at runtime, which is exactly why following the message can never reach
        // them. They are identified by implementing an interface from the dispatcher's own assembly
        // whose type arguments are still type parameters — the shape of a behaviour in any library.
        if (sawMessage && target.OriginalDefinition.ContainingAssembly is { } dispatcherAssembly)
        {
            foreach (var behaviour in FindPipelineTypes(dispatcherAssembly))
            {
                foreach (var method in behaviour.GetMembers().OfType<IMethodSymbol>())
                {
                    VisitMethod(method, depth + 1, walk);
                }
            }
        }
    }

    private List<INamedTypeSymbol> FindPipelineTypes(IAssemblySymbol dispatcherAssembly) =>
        _pipelines.GetOrAdd(dispatcherAssembly, assembly =>
        {
            var result = new List<INamedTypeSymbol>();
            foreach (var type in GetSourceTypes())
            {
                if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct))
                {
                    continue;
                }

                // Generic over the request: the interface comes from the dispatcher's assembly and is
                // constructed with the implementer's own type parameters. A handler closes the same
                // interface with a concrete message and must NOT match here — walking it would leak
                // one endpoint's failures into every other endpoint behind the same dispatcher.
                var isPipeline = type.AllInterfaces.Any(i =>
                    i.IsGenericType
                    && SymbolEqualityComparer.Default.Equals(i.OriginalDefinition.ContainingAssembly, assembly)
                    && i.TypeArguments.Any(a => a.TypeKind == TypeKind.TypeParameter));

                if (isPipeline)
                {
                    result.Add(type);
                }
            }

            return result;
        });

    /// <summary>
    /// A call worth reinterpreting: dispatched through an abstraction, with no implementation in the
    /// compilation to follow. A static or concrete call into a referenced assembly is not a dispatch,
    /// and treating it as one would turn every <c>Console.WriteLine</c> into a guess.
    /// </summary>
    private bool IsUnresolvedDispatchShape(IMethodSymbol target)
    {
        var definition = target.OriginalDefinition;

        var dispatched = definition.ContainingType?.TypeKind == TypeKind.Interface || definition.IsAbstract;

        return dispatched && !FindImplementations(definition).Any();
    }

    private List<INamedTypeSymbol> FindHandlersFor(INamedTypeSymbol message)
    {
        if (_handlers.TryGetValue(message, out var cached))
        {
            return cached;
        }

        var result = new List<INamedTypeSymbol>();
        foreach (var type in GetSourceTypes())
        {
            if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct)
                || SymbolEqualityComparer.Default.Equals(type, message))
            {
                continue;
            }

            // The interface shape: a generic interface constructed with the message, which MediatR,
            // Brighter and Wolverine's interface mode all share.
            var handles = type.AllInterfaces.Any(i =>
                i.IsGenericType && i.TypeArguments.Any(a => SymbolEqualityComparer.Default.Equals(a, message)));

            // The convention shape: Wolverine resolves `OrderHandler.Handle(PlaceOrder)` with no
            // interface at all. The suffix plus the method-name convention keeps this from swallowing
            // every method that merely takes the message as a parameter.
            var handlesByConvention = !handles
                && (type.Name.EndsWith("Handler", StringComparison.Ordinal)
                    || type.Name.EndsWith("Consumer", StringComparison.Ordinal))
                && type.GetMembers().OfType<IMethodSymbol>().Any(m =>
                    m.Name is "Handle" or "HandleAsync" or "Consume" or "ConsumeAsync"
                    && m.Parameters.Length > 0
                    && SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, message));

            if (handles || handlesByConvention)
            {
                result.Add(type);
            }
        }

        _handlers[message] = result;
        return result;
    }

    private IEnumerable<IMethodSymbol> FindImplementations(IMethodSymbol method)
    {
        var declaringType = method.ContainingType;
        if (declaringType is null)
        {
            yield break;
        }

        foreach (var candidate in GetCandidateTypes(declaringType))
        {
            var implementation = declaringType.TypeKind == TypeKind.Interface
                ? candidate.FindImplementationForInterfaceMember(method) as IMethodSymbol
                : candidate.GetMembers(method.Name).OfType<IMethodSymbol>()
                    .FirstOrDefault(m => SymbolEqualityComparer.Default.Equals(m.OverriddenMethod?.OriginalDefinition, method));

            if (implementation is not null && !SymbolEqualityComparer.Default.Equals(implementation, method))
            {
                yield return implementation;
            }
        }
    }

    private List<INamedTypeSymbol> GetCandidateTypes(INamedTypeSymbol declaringType)
    {
        if (_implementations.TryGetValue(declaringType, out var cached))
        {
            return cached;
        }

        var result = new List<INamedTypeSymbol>();
        foreach (var type in GetSourceTypes())
        {
            if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct))
            {
                continue;
            }

            var matches = declaringType.TypeKind == TypeKind.Interface
                ? type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, declaringType))
                : InheritsFrom(type, declaringType);

            if (matches)
            {
                result.Add(type);
            }
        }

        _implementations[declaringType] = result;
        return result;
    }

    private static bool InheritsFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, baseType))
            {
                return true;
            }
        }

        return false;
    }

    private List<INamedTypeSymbol> GetSourceTypes() => _sourceTypes.Value;

    private List<INamedTypeSymbol> CollectSourceTypes()
    {
        var types = new List<INamedTypeSymbol>();
        var queue = new Queue<INamespaceOrTypeSymbol>();
        queue.Enqueue(_compilation.Assembly.GlobalNamespace);

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
                    types.Add(type);
                    queue.Enqueue(type);
                }
            }
        }

        return types;
    }

    private SemanticModel GetModel(SyntaxTree tree) =>
        _models.GetOrAdd(tree, t => _compilation.GetSemanticModel(t));

    /// <summary>
    /// Cheap attribute identity check: the short name first (a string the symbol already holds), the
    /// namespace only on a name match. Building a display string per attribute per visited symbol is
    /// where a whole-compilation walk goes to die.
    /// </summary>
    private static bool IsErrorApiAttribute(AttributeData attribute, string shortName) =>
        attribute.AttributeClass is { } cls
        && cls.Name == shortName
        && cls.ContainingNamespace is { IsGlobalNamespace: false } ns
        && ns.Name == "ErrorApi"
        && ns.ContainingNamespace.IsGlobalNamespace;

    private static void AddDeclaredCodes(ISymbol symbol, SortedSet<string> codes)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (IsErrorApiAttribute(attribute, "ProducesErrorAttribute")
                && attribute.ConstructorArguments.Length > 0
                && attribute.ConstructorArguments[0].Value is string code)
            {
                codes.Add(code);
            }
        }
    }

    private bool TryGetMappedCode(ITypeSymbol type, out string? code) =>
        MappedTypes.TryGetValue(type.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), out code);

    private bool TryGetErrorCode(ISymbol symbol, out string? code)
    {
        var definition = symbol.OriginalDefinition;

        foreach (var attribute in definition.GetAttributes())
        {
            if (!IsErrorApiAttribute(attribute, "ErrorAttribute"))
            {
                continue;
            }

            var status = attribute.ConstructorArguments.Length switch
            {
                1 => attribute.ConstructorArguments[0].Value as int?,
                2 => attribute.ConstructorArguments[1].Value as int?,
                _ => null,
            };

            if (status is null)
            {
                continue;
            }

            // The same resolution the catalog parser applies, so the walk and the catalog agree.
            var value = NameInference.ResolveCode(definition, attribute, _compilation, GetModel);
            code = value;

            if (!Discovered.ContainsKey(value))
            {
                string? title = null, detail = null, description = null;
                foreach (var named in attribute.NamedArguments)
                {
                    var text = named.Value.Value as string;
                    switch (named.Key)
                    {
                        case "Title": title = text; break;
                        case "Detail": detail = text; break;
                        case "Description": description = text; break;
                    }
                }

                Discovered[value] = new DiscoveredError(
                    value,
                    status.Value,
                    title ?? NameInference.Humanize(NameInference.EntryName(definition)),
                    detail,
                    description,
                    definition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat));
            }

            return true;
        }

        code = null;
        return false;
    }
}
