using System.Text.Json;
using ErrorApi.AspNetCore;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ErrorApi.Generator.Tests;

/// <summary>
/// <c>HandleExceptions(h =&gt; h.Add(...))</c>: global handlers for exceptions the catalog does not
/// know. The generator half — a handler is reachable from everywhere, so what its <c>Map</c> reads
/// is documented on every endpoint — is pinned here.
/// </summary>
public sealed class GlobalExceptionHandlerDiscoveryTests
{
    private const string Catalog = """
        using System;
        using ErrorApi;
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Http;
        using Microsoft.AspNetCore.Routing;

        namespace Shop;

        [ErrorCatalog("Db")]
        public static partial class DbErrors
        {
            [Error(409)]
            public static partial Error Duplicate { get; }

            [Error(503)]
            public static partial Error Unavailable { get; }
        }

        public sealed class DriverException(int number) : Exception($"Driver error {number}.")
        {
            public int Number { get; } = number;
        }

        public static class Endpoints
        {
            public static void Map(IEndpointRouteBuilder app) => app.MapGet("/health", () => Results.Ok());
        }
        """;

    private static void AssertDocumentedEverywhere(GeneratorOutput output, string code, string undocumented)
    {
        Assert.DoesNotContain(output.GeneratorDiagnostics, d => d.Id == "EAPI010" && d.GetMessage().Contains(code, StringComparison.Ordinal));
        Assert.Contains(output.GeneratorDiagnostics, d => d.Id == "EAPI010" && d.GetMessage().Contains(undocumented, StringComparison.Ordinal));

        // Errors are indexed by code: Db.Duplicate = 0, Db.Unavailable = 1.
        var index = code == "Db.Duplicate" ? 0 : 1;
        Assert.Contains(
            $"new global::ErrorApi.EndpointErrors(\"GET\", \"/health\", new global::ErrorApi.ErrorDescriptor[] {{ _errors[{index}] }})",
            output.Source("ErrorApi.Metadata.g.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_typed_handler_class_is_walked_and_documented_everywhere()
    {
        const string setup = """
            using System;
            using ErrorApi;
            using ErrorApi.AspNetCore;
            using Microsoft.Extensions.DependencyInjection;

            namespace Shop;

            public sealed class DriverHandler : IGlobalExceptionHandler<DriverException>
            {
                public Error Map(DriverException e) => e.Number == 2627 ? DbErrors.Duplicate : Error.None;
            }

            public static class Setup
            {
                public static void Configure(IServiceCollection services) =>
                    services.AddErrorApi(x => x.HandleExceptions(h => h.Add<DriverException, DriverHandler>()));
            }
            """;

        AssertDocumentedEverywhere(GeneratorHarness.RunAndCompile(Catalog, setup), "Db.Duplicate", "Db.Unavailable");
    }

    [Fact]
    public void An_untyped_handler_class_is_walked_too()
    {
        const string setup = """
            using System;
            using ErrorApi;
            using ErrorApi.AspNetCore;
            using Microsoft.Extensions.DependencyInjection;

            namespace Shop;

            public sealed class OutageHandler : IGlobalExceptionHandler
            {
                public Error Map(Exception e) => e is TimeoutException ? DbErrors.Unavailable : Error.None;
            }

            public static class Setup
            {
                public static void Configure(IServiceCollection services) =>
                    services.AddErrorApi(x => x.HandleExceptions(h => h.Add<OutageHandler>()));
            }
            """;

        AssertDocumentedEverywhere(GeneratorHarness.RunAndCompile(Catalog, setup), "Db.Unavailable", "Db.Duplicate");
    }

    [Fact]
    public void A_lambda_handler_is_walked_in_place()
    {
        const string setup = """
            using System;
            using ErrorApi;
            using ErrorApi.AspNetCore;
            using Microsoft.Extensions.DependencyInjection;

            namespace Shop;

            public static class Setup
            {
                public static void Configure(IServiceCollection services) =>
                    services.AddErrorApi(x => x.HandleExceptions(h => h
                        .Add(e => e is TimeoutException ? DbErrors.Unavailable : Error.None)));
            }
            """;

        AssertDocumentedEverywhere(GeneratorHarness.RunAndCompile(Catalog, setup), "Db.Unavailable", "Db.Duplicate");
    }
}

/// <summary>The runtime half: the order is catalog, handlers as added, fallback.</summary>
[Collection("ambient-metadata")]
public sealed class GlobalExceptionHandlerPipelineTests
{
    private sealed class OrderNotFoundException : Exception;

    private sealed class DriverException(int number) : Exception($"Driver error {number}.")
    {
        public int Number { get; } = number;
    }

    private static readonly Error Duplicate = new("Db.Duplicate", 409, "Duplicate");
    private static readonly Error Unavailable = new("Db.Unavailable", 503, "Unavailable");

    private sealed class DriverHandler : IGlobalExceptionHandler<DriverException>
    {
        public Error Map(DriverException e) => e.Number == 2627 ? Duplicate : Error.None;
    }

    private static IExceptionHandler Build(Action<ExceptionHandlingBuilder> configure)
    {
        var metadata = new FakeMetadata();
        metadata.ByType[typeof(OrderNotFoundException)] = FakeMetadata.NotFound;

        var services = new ServiceCollection();
        services.AddLogging();

        using (ErrorApiRuntime.Use(metadata))
        {
            ErrorApiRegistration.Register(services, metadata, x => x.HandleExceptions(configure));
        }

        return services.BuildServiceProvider().GetServices<IExceptionHandler>().OfType<ErrorApiExceptionHandler>().Single();
    }

    private static HttpContext Context()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<(bool Handled, int Status, string? Code)> Run(IExceptionHandler handler, Exception exception)
    {
        var context = Context();
        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);
        if (!handled)
        {
            return (false, 0, null);
        }

        context.Response.Body.Position = 0;
        var code = JsonDocument.Parse(context.Response.Body).RootElement.GetProperty("code").GetString();
        return (true, context.Response.StatusCode, code);
    }

    private static readonly Action<ExceptionHandlingBuilder> FullPipeline = h => h
        .Add<DriverException, DriverHandler>()
        .Add(e => e is TimeoutException ? Unavailable : Error.None)
        .MapUnhandledException();

    [Fact]
    public async Task The_catalog_answers_before_any_handler()
    {
        var handler = Build(h => h.Add(_ => Unavailable));

        Assert.Equal((true, 404, "Orders.NotFound"), await Run(handler, new OrderNotFoundException()));
    }

    [Fact]
    public async Task A_typed_handler_answers_for_its_type_and_passes_the_rest_on()
    {
        var handler = Build(FullPipeline);

        Assert.Equal((true, 409, "Db.Duplicate"), await Run(handler, new DriverException(2627)));
        // Number 1 is passed on by the typed handler, skipped by the lambda, caught by the fallback.
        Assert.Equal((true, 500, "Server.Unhandled"), await Run(handler, new DriverException(1)));
    }

    [Fact]
    public async Task Handlers_run_in_the_order_they_were_added()
    {
        var handler = Build(h => h
            .Add(e => e is DriverException ? Unavailable : Error.None)
            .Add<DriverException, DriverHandler>());

        // The lambda came first and claims every DriverException, so the typed handler never sees 2627.
        Assert.Equal((true, 503, "Db.Unavailable"), await Run(handler, new DriverException(2627)));
    }

    [Fact]
    public async Task The_lambda_form_answers_and_the_fallback_takes_the_rest()
    {
        var handler = Build(FullPipeline);

        Assert.Equal((true, 503, "Db.Unavailable"), await Run(handler, new TimeoutException()));
        Assert.Equal((true, 500, "Server.Unhandled"), await Run(handler, new InvalidOperationException()));
    }

    [Fact]
    public async Task Without_a_fallback_what_no_handler_claims_is_left_alone()
    {
        var handler = Build(h => h.Add<DriverException, DriverHandler>());

        Assert.Equal((false, 0, null), await Run(handler, new InvalidOperationException()));
    }

    [Fact]
    public void The_block_allows_one_fallback()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Build(h => h.MapUnhandledException().MapUnhandledException(Unavailable)));
    }

    [Fact]
    public void The_obsolete_alias_still_registers_and_tunes()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        using (ErrorApiRuntime.Use(new FakeMetadata()))
        {
#pragma warning disable CS0618 // The alias is kept for one release on purpose; this is its test.
            ErrorApiRegistration.Register(services, new FakeMetadata(), x => x.AddExceptionHandler(o => o.UseExceptionMessageAsDetail = false));
#pragma warning restore CS0618
        }

        var provider = services.BuildServiceProvider();
        Assert.Contains(provider.GetServices<IExceptionHandler>(), h => h is ErrorApiExceptionHandler);
        Assert.False(provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ErrorApiExceptionOptions>>().Value.UseExceptionMessageAsDetail);
    }
}
