using System.Collections.Generic;
using System.Linq;

namespace ErrorApi.Generator.Tests;

/// <summary>
/// A hand-built model, so runtime mapping can be tested without standing up a host or running the
/// generator. Deliberately small: two codes, three endpoints, one of them error-free.
/// </summary>
public sealed class FakeMetadata : IErrorApiMetadata
{
    /// <summary>The 404 entry.</summary>
    public static readonly ErrorDescriptor NotFound =
        new("Orders.NotFound", 404, "Order not found", null, "No order exists for that id.", "Shop.OrderErrors.NotFound");

    /// <summary>The 409 entry.</summary>
    public static readonly ErrorDescriptor AlreadyPaid =
        new("Orders.AlreadyPaid", 409, "Order already paid", "Order {orderId} was already paid.", null, "Shop.OrderErrors.AlreadyPaid");

    /// <inheritdoc />
    public IReadOnlyList<ErrorDescriptor> AllErrors { get; } = [NotFound, AlreadyPaid];

    /// <inheritdoc />
    public IReadOnlyList<EndpointErrors> Endpoints { get; } =
    [
        new("GET", "/orders/{id}", [NotFound]),
        new("POST", "/orders/{id}/pay", [NotFound, AlreadyPaid]),
        new("GET", "/health", []),
    ];

    /// <summary>Types this fake resolves by instance, standing in for the generated pattern switch.</summary>
    public Dictionary<Type, ErrorDescriptor> ByType { get; } = [];

    /// <inheritdoc />
    public ErrorDescriptor? FindError(string code) => AllErrors.FirstOrDefault(e => e.Code == code);

    /// <inheritdoc />
    public ErrorDescriptor? FindErrorForInstance(object? instance) =>
        instance is not null && ByType.TryGetValue(instance.GetType(), out var descriptor) ? descriptor : null;

    /// <inheritdoc />
    public bool TryGetEndpointErrors(string httpMethod, string routePattern, out IReadOnlyList<ErrorDescriptor> errors) =>
        TryGetEndpointErrors(httpMethod, routePattern, group: null, out errors);

    /// <inheritdoc />
    public bool TryGetEndpointErrors(string httpMethod, string routePattern, string? group, out IReadOnlyList<ErrorDescriptor> errors)
    {
        // The same resolution the generated model applies: exact group (matched through
        // EndpointGroup.Normalize, like the generated switch), then the ungrouped entry, and a null
        // group also matches a route that lives in exactly one group.
        var candidates = Endpoints
            .Where(e => e.HttpMethod == httpMethod && e.RoutePattern == routePattern)
            .ToList();

        var normalized = EndpointGroup.Normalize(group);
        var match = (normalized is null ? null : candidates.FirstOrDefault(e => EndpointGroup.Normalize(e.Group) == normalized))
            ?? candidates.FirstOrDefault(e => e.Group is null)
            ?? (group is null && candidates.Count == 1 ? candidates[0] : null);

        errors = match?.Errors ?? [];
        return match is not null;
    }
}
