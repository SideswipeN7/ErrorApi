using ErrorApi.Generator.Helpers;

namespace ErrorApi.Generator.Model;

/// <summary>How the generator treats one <c>[Error]</c> declaration.</summary>
internal enum CatalogEntryKind
{
    /// <summary>A <c>static partial Error</c> member: the generator writes its implementation.</summary>
    Generated,

    /// <summary>
    /// Anything else — a type, a field, or a member the caller implements itself. The generator writes
    /// no body; it only records the entry and teaches the walker to recognise it. This is what lets a
    /// catalog be expressed in another library's error type.
    /// </summary>
    Declared,
}

/// <summary>One catalog member parameter, as needed to re-declare a generated catalog method.</summary>
internal sealed record ParameterModel(string TypeDisplay, string Name) : System.IEquatable<ParameterModel>;

/// <summary>
/// One <c>[Error]</c> declaration, flattened into everything the emitters need: the values from the
/// attribute plus, for generated entries, the exact modifiers and nesting required to write the
/// implementing partial.
/// </summary>
internal sealed record CatalogEntry(
    CatalogEntryKind Kind,
    string Code,
    int StatusCode,
    string? Title,
    string? Detail,
    string? Description,
    string Namespace,
    EquatableArray<string> ContainerDeclarations,
    string MemberModifiers,
    string MemberName,
    bool IsMethod,
    EquatableArray<ParameterModel> Parameters,
    string DeclaringMember,
    string? ErrorTypeDisplay,
    LocationInfo? Location,
    string? ExportId = null,
    EquatableArray<string> Suppressions = default,
    bool ExportsStatus = false) : System.IEquatable<CatalogEntry>
{
    /// <summary>
    /// The fully qualified type this entry was declared on, when <c>[Error]</c> sits on a type that can
    /// be pattern-matched at runtime. Drives the generated instance-to-error switch.
    /// </summary>
    public bool IsErrorType => ErrorTypeDisplay is not null;
}
