using System.Collections.Generic;
using System.Collections.Immutable;
using ErrorApi.Generator.Helpers;

namespace ErrorApi.Generator.Model;

/// <summary>
/// What one endpoint candidate contributed to the scan — possibly an endpoint, possibly diagnostics.
/// Produced by both endpoint surfaces (Minimal API call sites and controller actions) and merged by
/// <c>EndpointScanner</c> through one code path.
/// </summary>
internal sealed record ScannedEndpoint(
    EndpointModel? Endpoint,
    string? UnresolvedDispatch,
    bool HandlerReturnsResult,
    ImmutableHashSet<string> Suppressed,
    List<DiagnosticInfo> Diagnostics);
