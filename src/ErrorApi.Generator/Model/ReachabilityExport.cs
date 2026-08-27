using ErrorApi.Generator.Helpers;

namespace ErrorApi.Generator.Model;

/// <summary>
/// One <c>[assembly: ReachabilityExport]</c> line to be baked into this assembly: the documentation
/// comment ID of a member or message type, and the catalog codes reachable from it.
/// </summary>
internal sealed record ReachabilityExport(
    string MemberId,
    EquatableArray<string> Codes) : System.IEquatable<ReachabilityExport>;
