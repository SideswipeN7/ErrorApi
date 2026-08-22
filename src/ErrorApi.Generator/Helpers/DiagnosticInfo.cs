using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace ErrorApi.Generator.Helpers;

/// <summary>An equatable stand-in for <see cref="Diagnostic"/>, materialized only at source-output time.</summary>
internal sealed record DiagnosticInfo(DiagnosticDescriptor Descriptor, LocationInfo? Location, EquatableArray<string> Arguments)
{
    public static DiagnosticInfo Create(DiagnosticDescriptor descriptor, SyntaxNode? node, params string[] arguments) =>
        new(descriptor, LocationInfo.From(node), new EquatableArray<string>(arguments.ToImmutableArray()));

    public static DiagnosticInfo Create(DiagnosticDescriptor descriptor, LocationInfo? location, params string[] arguments) =>
        new(descriptor, location, new EquatableArray<string>(arguments.ToImmutableArray()));

    public Diagnostic ToDiagnostic() =>
        Diagnostic.Create(Descriptor, Location?.ToLocation(), Arguments.AsImmutableArray().CastArray<object?>().ToArray());
}
