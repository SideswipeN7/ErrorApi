using System.Collections.Generic;

namespace ErrorApi;

/// <summary>Compile-time description of one catalog entry, used to document the API.</summary>
public sealed class ErrorDescriptor
{
    /// <param name="code">Stable machine-readable code.</param>
    /// <param name="statusCode">HTTP status code this error maps to.</param>
    /// <param name="title">Short human-readable summary.</param>
    /// <param name="detail">Detail template declared on the catalog entry, if any.</param>
    /// <param name="description">Longer prose for documentation output.</param>
    /// <param name="declaringMember">Fully qualified catalog member, e.g. <c>Sample.OrderErrors.NotFound</c>.</param>
    public ErrorDescriptor(string code, int statusCode, string? title, string? detail, string? description, string declaringMember)
    {
        Code = code;
        StatusCode = statusCode;
        Title = title;
        Detail = detail;
        Description = description;
        DeclaringMember = declaringMember;
    }

    /// <summary>Stable machine-readable code.</summary>
    public string Code { get; }

    /// <summary>HTTP status code this error maps to.</summary>
    public int StatusCode { get; }

    /// <summary>Short human-readable summary.</summary>
    public string? Title { get; }

    /// <summary>Detail template declared on the catalog entry, if any.</summary>
    public string? Detail { get; }

    /// <summary>Longer prose for documentation output.</summary>
    public string? Description { get; }

    /// <summary>Fully qualified catalog member the entry was declared on.</summary>
    public string DeclaringMember { get; }

    /// <summary>Materializes the runtime <see cref="ErrorApi.Error"/> this entry describes.</summary>
    public Error ToError() => new(Code, StatusCode, Title, Detail);
}

/// <summary>The set of errors one endpoint can return, as discovered at compile time.</summary>
public sealed class EndpointErrors
{
    /// <param name="httpMethod">Upper-case HTTP method, e.g. <c>GET</c>.</param>
    /// <param name="routePattern">Normalized route pattern, e.g. <c>/orders/{id}</c>.</param>
    /// <param name="errors">Errors reachable from the endpoint handler.</param>
    /// <param name="group">API description group the endpoint belongs to, or <see langword="null"/>.</param>
    public EndpointErrors(string httpMethod, string routePattern, IReadOnlyList<ErrorDescriptor> errors, string? group = null)
    {
        HttpMethod = httpMethod;
        RoutePattern = routePattern;
        Errors = errors;
        Group = group;
    }

    /// <summary>Upper-case HTTP method, e.g. <c>GET</c>.</summary>
    public string HttpMethod { get; }

    /// <summary>Normalized route pattern, e.g. <c>/orders/{id}</c>.</summary>
    public string RoutePattern { get; }

    /// <summary>
    /// The API description group the endpoint belongs to — <c>WithGroupName(...)</c> on the endpoint or
    /// its group, or <c>[ApiExplorerSettings(GroupName = ...)]</c> on a controller. This is what
    /// separates two endpoints that share a route and method but answer for different API versions.
    /// <see langword="null"/> for the common ungrouped endpoint.
    /// </summary>
    public string? Group { get; }

    /// <summary>Errors reachable from the endpoint handler.</summary>
    public IReadOnlyList<ErrorDescriptor> Errors { get; }
}

/// <summary>
/// The compile-time error model of one assembly. The implementation is emitted by the ErrorApi
/// source generator; nothing here is discovered by reflection.
/// </summary>
public interface IErrorApiMetadata
{
    /// <summary>Every entry in the error catalog.</summary>
    IReadOnlyList<ErrorDescriptor> AllErrors { get; }

    /// <summary>Every endpoint the generator matched, with the errors it can return.</summary>
    IReadOnlyList<EndpointErrors> Endpoints { get; }

    /// <summary>Looks up one catalog entry. Returns <see langword="null"/> for an unknown code.</summary>
    ErrorDescriptor? FindError(string code);

    /// <summary>
    /// Maps an error object onto its catalog entry by matching its type against the <c>[Error]</c>-annotated
    /// types of this assembly. The generated implementation is a pattern switch over those types, so this
    /// works under trimming and native AOT; it is what lets an adapter turn another library's error value
    /// into an <see cref="ErrorApi.Error"/>. Returns <see langword="null"/> for an unrecognized instance.
    /// </summary>
    ErrorDescriptor? FindErrorForInstance(object? instance);

    /// <summary>Looks up the errors of one endpoint. <paramref name="routePattern"/> is normalized by <see cref="RoutePattern.Normalize"/>.</summary>
    bool TryGetEndpointErrors(string httpMethod, string routePattern, out IReadOnlyList<ErrorDescriptor> errors);

    /// <summary>
    /// Looks up the errors of one endpoint within an API description group. Resolution order: the exact
    /// group first, then the ungrouped entry for the same route and method; a <see langword="null"/>
    /// <paramref name="group"/> also matches a route that exists in exactly one group, so a purely
    /// cosmetic <c>WithGroupName</c> never hides an endpoint's errors.
    /// </summary>
    bool TryGetEndpointErrors(string httpMethod, string routePattern, string? group, out IReadOnlyList<ErrorDescriptor> errors);
}
