using System.Collections.Generic;
using System.Linq;

namespace ErrorApi;

/// <summary>
/// Several compile-time models answering as one — the shape behind
/// <c>AddErrorApi(x =&gt; x.Include(...))</c>. Lookups try the models in registration order and the
/// first answer wins, so the host assembly's own model stays authoritative and the included ones fill
/// in what it cannot know: most importantly, the instance-type switches of referenced assemblies,
/// which resolve failures whose types are declared there.
/// </summary>
public sealed class CompositeErrorApiMetadata : IErrorApiMetadata
{
    private readonly IReadOnlyList<IErrorApiMetadata> _models;

    /// <param name="models">The models to compose, in priority order.</param>
    public CompositeErrorApiMetadata(IReadOnlyList<IErrorApiMetadata> models)
    {
        if (models is null)
        {
            throw new ArgumentNullException(nameof(models));
        }

        if (models.Count == 0)
        {
            throw new ArgumentException("At least one model is required.", nameof(models));
        }

        _models = models;
    }

    /// <inheritdoc />
    public IReadOnlyList<ErrorDescriptor> AllErrors
    {
        get
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var merged = new List<ErrorDescriptor>();

            foreach (var model in _models)
            {
                foreach (var error in model.AllErrors)
                {
                    if (seen.Add(error.Code))
                    {
                        merged.Add(error);
                    }
                }
            }

            return merged;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<EndpointErrors> Endpoints => _models.SelectMany(m => m.Endpoints).ToList();

    /// <inheritdoc />
    public ErrorDescriptor? FindError(string code)
    {
        foreach (var model in _models)
        {
            if (model.FindError(code) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public ErrorDescriptor? FindErrorForInstance(object? instance)
    {
        foreach (var model in _models)
        {
            if (model.FindErrorForInstance(instance) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public bool TryGetEndpointErrors(string httpMethod, string routePattern, out IReadOnlyList<ErrorDescriptor> errors) =>
        TryGetEndpointErrors(httpMethod, routePattern, group: null, out errors);

    /// <inheritdoc />
    public bool TryGetEndpointErrors(string httpMethod, string routePattern, string? group, out IReadOnlyList<ErrorDescriptor> errors)
    {
        foreach (var model in _models)
        {
            if (model.TryGetEndpointErrors(httpMethod, routePattern, group, out errors))
            {
                return true;
            }
        }

        errors = System.Array.Empty<ErrorDescriptor>();
        return false;
    }
}
