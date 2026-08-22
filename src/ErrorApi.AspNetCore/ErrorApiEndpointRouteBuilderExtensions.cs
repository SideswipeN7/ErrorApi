using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace ErrorApi.AspNetCore;

/// <summary>Serves the generated error contract straight from the running API.</summary>
public static class ErrorApiEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps an endpoint returning the TypeScript error contract. Pointing the frontend build at it
    /// keeps the client's error union in step with the API without a separate publishing step.
    /// </summary>
    /// <param name="endpoints">The route builder.</param>
    /// <param name="pattern">Route to serve the module from.</param>
    public static IEndpointConventionBuilder MapErrorContract(this IEndpointRouteBuilder endpoints, string pattern = "/openapi/errors.ts")
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        // A RequestDelegate keeps this endpoint out of the reflection-based parameter binder, so the
        // library stays trimmable and AOT-clean.
        RequestDelegate handler = static async context =>
        {
            var metadata = context.RequestServices.GetRequiredService<IErrorApiMetadata>();
            context.Response.ContentType = "application/typescript; charset=utf-8";
            await context.Response.WriteAsync(TypeScriptContractWriter.Write(metadata), context.RequestAborted);
        };

        // EAPI002 is silenced for this project in the csproj: generator diagnostics ignore #pragma.
        return endpoints.MapGet(pattern, handler)
            .ExcludeFromDescription()
            .WithName("ErrorApiContract");
    }

    /// <summary>
    /// Writes the TypeScript contract to <paramref name="path"/> when the process was started with
    /// <c>--emit-error-contract</c>, then reports whether the host should stop instead of serving.
    /// </summary>
    /// <example>
    /// <code>
    /// if (app.TryEmitErrorContract(args)) return;
    /// app.Run();
    /// </code>
    /// </example>
    public static bool TryEmitErrorContract(this IApplicationBuilder app, string[] args, string path = "errors.ts")
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(args);

        var index = Array.IndexOf(args, "--emit-error-contract");
        if (index < 0)
        {
            return false;
        }

        if (index + 1 < args.Length && !args[index + 1].StartsWith('-'))
        {
            path = args[index + 1];
        }

        var metadata = app.ApplicationServices.GetRequiredService<IErrorApiMetadata>();
        File.WriteAllText(path, TypeScriptContractWriter.Write(metadata));
        Console.WriteLine($"ErrorApi: wrote {metadata.AllErrors.Count} codes for {metadata.Endpoints.Count} endpoints to {Path.GetFullPath(path)}");
        return true;
    }
}
