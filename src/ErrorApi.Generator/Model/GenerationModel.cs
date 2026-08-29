using ErrorApi.Generator.Helpers;

namespace ErrorApi.Generator.Model;

/// <summary>
/// Everything one run of the pipeline decided, reduced to value-equatable data. This is the boundary
/// that makes the generator incremental where it can be: the walk itself has to look at the whole
/// compilation, but an edit that does not change this model leaves the emit step cached.
/// </summary>
internal sealed record GenerationModel(
    bool HasAbstractions,
    bool HasRegistrationType,
    EquatableArray<CatalogEntry> Entries,
    EquatableArray<DiscoveredError> Errors,
    EquatableArray<EndpointModel> Endpoints,
    EquatableArray<DiagnosticInfo> Diagnostics,
    EquatableArray<ReachabilityExport> Reachability = default,
    string AssemblyName = "") : System.IEquatable<GenerationModel>
{
    /// <summary>The model of a compilation that does not reference ErrorApi.Abstractions.</summary>
    public static readonly GenerationModel Empty = new(
        HasAbstractions: false,
        HasRegistrationType: false,
        Entries: EquatableArray<CatalogEntry>.Empty,
        Errors: EquatableArray<DiscoveredError>.Empty,
        Endpoints: EquatableArray<EndpointModel>.Empty,
        Diagnostics: EquatableArray<DiagnosticInfo>.Empty);
}
