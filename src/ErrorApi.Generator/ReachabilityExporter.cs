using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using ErrorApi.Generator.Helpers;
using ErrorApi.Generator.Model;
using Microsoft.CodeAnalysis;

namespace ErrorApi.Generator;

/// <summary>
/// Walks a library's own public surface and records which catalog codes each member can reach, so a
/// consuming compilation can continue the walk across the assembly boundary. Two kinds of entry come
/// out: a method's reachability (for direct calls into the library) and a message type's reachability
/// (for dispatches whose handler lives here). Both are emitted as
/// <c>[assembly: ReachabilityExport]</c> and read back by <see cref="ErrorReachabilityWalker"/>.
/// </summary>
internal static class ReachabilityExporter
{
    public static List<ReachabilityExport> Compute(
        Compilation compilation,
        IReadOnlyDictionary<string, string> mappedTypes,
        System.Threading.CancellationToken cancellationToken)
    {
        var walker = new ErrorReachabilityWalker(compilation) { MappedTypes = mappedTypes };
        var types = CollectPublicTypes(compilation);
        var found = new ConcurrentBag<(string Id, SortedSet<string> Codes)>();

        System.Threading.Tasks.Parallel.ForEach(
            types,
            new System.Threading.Tasks.ParallelOptions { CancellationToken = cancellationToken },
            type => ExportType(type, walker, found));

        // Union per member id, deterministically ordered — the same source must always bake the same bytes.
        var merged = new SortedDictionary<string, SortedSet<string>>(System.StringComparer.Ordinal);
        foreach (var (id, codes) in found)
        {
            if (merged.TryGetValue(id, out var existing))
            {
                existing.UnionWith(codes);
            }
            else
            {
                merged[id] = codes;
            }
        }

        return merged
            .Select(pair => new ReachabilityExport(pair.Key, pair.Value.ToEquatableArray()))
            .ToList();
    }

    private static void ExportType(
        INamedTypeSymbol type, ErrorReachabilityWalker walker, ConcurrentBag<(string, SortedSet<string>)> found)
    {
        SortedSet<string>? typeCodes = null;

        foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
        {
            if (method.MethodKind != MethodKind.Ordinary || method.DeclaredAccessibility != Accessibility.Public)
            {
                continue;
            }

            var codes = walker.CollectFromMethod(method).Codes;
            if (codes.Count == 0)
            {
                continue;
            }

            if (method.GetDocumentationCommentId() is { } methodId)
            {
                found.Add((methodId, codes));
            }

            (typeCodes ??= new SortedSet<string>(System.StringComparer.Ordinal)).UnionWith(codes);
        }

        if (typeCodes is null || type.TypeKind is not (TypeKind.Class or TypeKind.Struct))
        {
            return;
        }

        // The handler shapes, exported under the *message* so a consumer's dispatch can find them:
        // a generic interface constructed with the message, or the *Handler/*Consumer convention.
        foreach (var message in MessagesHandledBy(type))
        {
            if (message.GetDocumentationCommentId() is { } messageId)
            {
                found.Add((messageId, typeCodes));
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> MessagesHandledBy(INamedTypeSymbol type)
    {
        var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var contract in type.AllInterfaces)
        {
            if (!contract.IsGenericType || IsFrameworkAssembly(contract.ContainingAssembly))
            {
                continue;
            }

            foreach (var argument in contract.TypeArguments)
            {
                if (IsMessageCandidate(argument, type) && seen.Add((INamedTypeSymbol)argument))
                {
                    yield return (INamedTypeSymbol)argument;
                }
            }
        }

        if (type.Name.EndsWith("Handler", System.StringComparison.Ordinal)
            || type.Name.EndsWith("Consumer", System.StringComparison.Ordinal))
        {
            foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
            {
                if (method.Name is "Handle" or "HandleAsync" or "Consume" or "ConsumeAsync"
                    && method.Parameters.Length > 0
                    && IsMessageCandidate(method.Parameters[0].Type, type)
                    && seen.Add((INamedTypeSymbol)method.Parameters[0].Type))
                {
                    yield return (INamedTypeSymbol)method.Parameters[0].Type;
                }
            }
        }
    }

    /// <summary>
    /// A type worth exporting reachability under: a class or struct of the application's own world.
    /// The handler itself is excluded (a record implements <c>IEquatable</c> of itself), and so is
    /// anything from the framework — a <c>Guid</c> is never a message.
    /// </summary>
    private static bool IsMessageCandidate(ITypeSymbol candidate, INamedTypeSymbol handler) =>
        candidate is INamedTypeSymbol { TypeKind: TypeKind.Class or TypeKind.Struct, SpecialType: SpecialType.None } named
        && !SymbolEqualityComparer.Default.Equals(named, handler)
        && !IsFrameworkAssembly(named.ContainingAssembly);

    private static bool IsFrameworkAssembly(IAssemblySymbol? assembly)
    {
        var name = assembly?.Name;
        return name is null or "mscorlib" or "netstandard" or "System"
            || name.StartsWith("System.", System.StringComparison.Ordinal)
            || name.StartsWith("Microsoft.", System.StringComparison.Ordinal);
    }

    private static List<INamedTypeSymbol> CollectPublicTypes(Compilation compilation)
    {
        var types = new List<INamedTypeSymbol>();
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
                    if (type.DeclaredAccessibility == Accessibility.Public)
                    {
                        types.Add(type);
                        queue.Enqueue(type);
                    }
                }
            }
        }

        return types;
    }
}
