using System.Collections.Generic;
using System.Linq;

namespace ErrorApi.AspNetCore;

/// <summary>
/// Shapes what a model <em>documents</em> without changing what the API <em>does</em>. The
/// documentation surfaces — <see cref="IErrorApiMetadata.AllErrors"/>,
/// <see cref="IErrorApiMetadata.Endpoints"/> and <c>TryGetEndpointErrors</c>, which feed the OpenAPI
/// transformer and the TypeScript writer — go through the visibility filter and, when descriptions are
/// disabled, lose the prose. The runtime surfaces — <c>FindError</c> and <c>FindErrorForInstance</c>,
/// which the adapters resolve responses through — pass straight to the inner model, so a hidden code
/// still answers exactly as before.
/// </summary>
internal sealed class DocumentFilteredMetadata : IErrorApiMetadata
{
    private readonly IErrorApiMetadata _inner;
    private readonly Func<ErrorDescriptor, bool>? _visible;
    private readonly bool _descriptionsEnabled;

    // Documentation is built a handful of times per process; the shaped descriptors are cached so the
    // same entry maps to one instance rather than a fresh copy per document pass.
    private readonly Dictionary<ErrorDescriptor, ErrorDescriptor> _shaped = [];
    private readonly object _gate = new();

    public DocumentFilteredMetadata(IErrorApiMetadata inner, Func<ErrorDescriptor, bool>? visible, bool descriptionsEnabled)
    {
        _inner = inner;
        _visible = visible;
        _descriptionsEnabled = descriptionsEnabled;
    }

    public IReadOnlyList<ErrorDescriptor> AllErrors => Shape(_inner.AllErrors);

    public IReadOnlyList<EndpointErrors> Endpoints =>
        _inner.Endpoints
            .Select(e => new EndpointErrors(e.HttpMethod, e.RoutePattern, Shape(e.Errors), e.Group))
            .ToList();

    public ErrorDescriptor? FindError(string code) => _inner.FindError(code);

    public ErrorDescriptor? FindErrorForInstance(object? instance) => _inner.FindErrorForInstance(instance);

    public bool TryGetEndpointErrors(string httpMethod, string routePattern, out IReadOnlyList<ErrorDescriptor> errors) =>
        TryGetEndpointErrors(httpMethod, routePattern, group: null, out errors);

    public bool TryGetEndpointErrors(string httpMethod, string routePattern, string? group, out IReadOnlyList<ErrorDescriptor> errors)
    {
        if (!_inner.TryGetEndpointErrors(httpMethod, routePattern, group, out var found))
        {
            errors = [];
            return false;
        }

        // The endpoint itself stays documented even when every one of its codes is hidden — an empty
        // list here simply leaves the operation without error responses.
        errors = Shape(found);
        return true;
    }

    private IReadOnlyList<ErrorDescriptor> Shape(IReadOnlyList<ErrorDescriptor> source)
    {
        var shaped = new List<ErrorDescriptor>(source.Count);

        foreach (var error in source)
        {
            if (_visible is not null && !_visible(error))
            {
                continue;
            }

            shaped.Add(_descriptionsEnabled || error.Description is null ? error : WithoutDescription(error));
        }

        return shaped;
    }

    private ErrorDescriptor WithoutDescription(ErrorDescriptor error)
    {
        lock (_gate)
        {
            if (!_shaped.TryGetValue(error, out var stripped))
            {
                _shaped[error] = stripped = new ErrorDescriptor(
                    error.Code, error.StatusCode, error.Title, error.Detail, description: null, error.DeclaringMember);
            }

            return stripped;
        }
    }
}
