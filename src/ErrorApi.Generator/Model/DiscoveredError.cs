namespace ErrorApi.Generator.Model;

/// <summary>
/// An <c>[Error]</c> declaration seen while walking a handler. Unlike <see cref="CatalogEntry"/> this
/// can also come from a referenced assembly, where only the attribute data is available.
/// </summary>
internal sealed record DiscoveredError(
    string Code,
    int StatusCode,
    string? Title,
    string? Detail,
    string? Description,
    string DeclaringMember) : System.IEquatable<DiscoveredError>;
