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

        services.TryAddSingleton(metadata);
        ErrorApiRuntime.Metadata = metadata;
        services.ConfigureAll<OpenApiOptions>(options => options.AddErrorResponses());

        return services;
    }
}
