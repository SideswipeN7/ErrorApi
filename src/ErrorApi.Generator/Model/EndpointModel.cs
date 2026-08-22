using ErrorApi.Generator.Helpers;

namespace ErrorApi.Generator.Model;

/// <summary>One <c>Map*</c> call site, with the error codes reachable from its handler.</summary>
internal sealed record EndpointModel(
    string HttpMethod,
    string RoutePattern,
    string DeclaredPattern,
    string HandlerDisplay,
    EquatableArray<string> ErrorCodes,
    LocationInfo? Location) : System.IEquatable<EndpointModel>;
