using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ErrorApi.AspNetCore;

/// <summary>
/// The entry point the generated <c>AddErrorApi()</c> overload calls into. Referenced by generated
/// code, so keep the signature stable.
/// </summary>
public static class ErrorApiRegistration
{
    /// <summary>
    /// Registers the compile-time error model and hooks the OpenAPI operation transformer into every
    /// OpenAPI document, so error responses are documented without any further wiring.
    /// </summary>
    public static IServiceCollection Register(IServiceCollection services, IErrorApiMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(metadata);

        // First registration wins — everywhere. TryAddSingleton keeps the first model, so the ambient
        // static must keep it too; letting a second AddErrorApi() overwrite the static would leave DI
        // and the adapters reading different models in a host with more than one registering module.
        // Composing models deliberately is what AddErrorApi(x => x.Include(...)) is for.
        if (services.Any(d => d.ServiceType == typeof(IErrorApiMetadata)))
        {
            return services;
        }

        services.AddSingleton(metadata);
        ErrorApiRuntime.Metadata = metadata;
        services.ConfigureAll<OpenApiOptions>(options => options.AddErrorResponses());

        return services;
    }

    /// <summary>
    /// The configurable form: the host assembly's model first, then whatever the options include —
    /// composed into one <see cref="CompositeErrorApiMetadata"/> where the first answer wins — and
    /// shaped for documentation when the options say so (descriptions off, codes filtered).
    /// </summary>
    public static IServiceCollection Register(IServiceCollection services, IErrorApiMetadata metadata, Action<ErrorApiOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentNullException.ThrowIfNull(metadata);

        var options = new ErrorApiOptions();
        configure(options);

        if (options.ExceptionHandlerEnabled)
        {
            services.AddErrorApiExceptionHandler(options.ExceptionHandlerConfigure);
        }

        var model = metadata;
        if (options.Included.Count > 0)
        {
            var models = new List<IErrorApiMetadata>(options.Included.Count + 1) { metadata };
            models.AddRange(options.Included);
            model = new CompositeErrorApiMetadata(models);
        }

        if (options.Visibility is not null || !options.DescriptionsEnabled)
        {
            model = new DocumentFilteredMetadata(model, options.Visibility, options.DescriptionsEnabled);
        }

        return Register(services, model);
    }
}
