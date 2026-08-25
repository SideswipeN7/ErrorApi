using Microsoft.CodeAnalysis;

namespace ErrorApi.Generator;

internal static class Diagnostics
{
    private const string Category = "ErrorApi";

    public static readonly DiagnosticDescriptor DuplicateErrorCode = new(
        id: "EAPI001",
        title: "Duplicate error code",
        messageFormat: "Error code '{0}' is declared more than once; it is also declared on '{1}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Error codes are the contract shipped to clients, so each one must resolve to a single status and title.");

    public static readonly DiagnosticDescriptor NonLiteralRoute = new(
        id: "EAPI002",
        title: "Route pattern is not a literal",
        messageFormat: "The route pattern is not a compile-time constant, so this endpoint cannot be documented; move the pattern into a const or add [ProducesError] to the handler",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The generator matches endpoints by their route template, which has to be known at compile time.");

    public static readonly DiagnosticDescriptor InvalidCatalogMember = new(
        id: "EAPI003",
        title: "Invalid error catalog member",
        messageFormat: "'{0}' cannot be an error catalog entry: {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "[Error] entries are implemented by the generator and must be declared as static partial members returning ErrorApi.Error.");

    public static readonly DiagnosticDescriptor InvalidStatusCode = new(
        id: "EAPI004",
        title: "Invalid HTTP status code",
        messageFormat: "Status code {0} on '{1}' is outside the 100-599 range",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The declared status code is emitted verbatim into the OpenAPI document and the HTTP response.");

    public static readonly DiagnosticDescriptor UnknownErrorCode = new(
        id: "EAPI005",
        title: "Unknown error code",
        messageFormat: "[ProducesError(\"{0}\")] does not match any [Error] declaration in this compilation",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Declared codes must exist in the catalog, otherwise the documented contract drifts from the code.");

    public static readonly DiagnosticDescriptor CodeDisagreesWithBody = new(
        id: "EAPI008",
        title: "Declared error code disagrees with the code in the body",
        messageFormat: "[Error] declares '{0}' but the member's body passes '{1}' as the code; the documented contract and the wire response would differ",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Drop the code from the attribute and let it be inferred from the body, or make the two agree.");

    public static readonly DiagnosticDescriptor UnreachableError = new(
        id: "EAPI010",
        title: "Declared error is not returned by any endpoint",
        messageFormat: "'{0}' is declared but no endpoint in this project can return it",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Either the entry is dead and should go, or it is raised behind something the walk cannot follow — a generic pipeline behaviour, a handler in another assembly — in which case the endpoints that surface it need [ProducesError]. A catalog meant to be consumed by other projects should live in a project of its own, where this rule stays silent because there are no endpoints to check against.");

    public static readonly DiagnosticDescriptor UnresolvedDispatch = new(
        id: "EAPI009",
        title: "The walk stopped at a dispatcher",
        messageFormat: "'{0} {1}' reaches '{2}', whose implementation is outside this compilation, and no handler was found for the message; failures raised behind it are not documented",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "An endpoint behind an unreadable dispatcher loses the failures raised past it — entirely, or worse, partially, which reads as a complete contract. Declare them with [ProducesError] (on the endpoint or on the message type), or keep the handler in the same compilation so it can be followed.");

    public static readonly DiagnosticDescriptor NoErrorsDiscovered = new(
        id: "EAPI006",
        title: "Endpoint declares no errors",
        messageFormat: "No error is reachable from the handler of '{0} {1}'; the endpoint will be documented with success responses only",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Endpoints that return Result<T> normally reach at least one catalog entry; none being found often means the handler was resolved through a boundary the generator cannot follow.");

    public static readonly DiagnosticDescriptor UnresolvedHandler = new(
        id: "EAPI007",
        title: "Endpoint handler could not be resolved",
        messageFormat: "The handler of '{0} {1}' could not be resolved to source, so its errors were not discovered; add [ProducesError] to declare them explicitly",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The generator follows the handler delegate into source; handlers coming from another assembly or from a runtime-built delegate are opaque to it.");
}

