using System.Text.Json;
using ErrorApi.AspNetCore;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace ErrorApi.Generator.Tests;

/// <summary>
/// <c>MapUnhandledException</c>: the fallback for exceptions the catalog does not know. Two halves,
/// pinned together — the handler answers with the entry, and the generator documents it on every
/// endpoint, because a fallback is reachable from everywhere by definition.
/// </summary>
public sealed class UnhandledExceptionTests
{
    private const string Catalog = """
        using System;
        using ErrorApi;
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Http;
        using Microsoft.AspNetCore.Routing;

        namespace Shop;

        [ErrorCatalog("Orders")]
        public static class OrderErrors
        {
            [Error(404)]
            public sealed class NotFoundException(Guid id) : Exception($"No order {id}.");
        }

        [ErrorCatalog("Server")]
        public static partial class ApiErrors
        {
            [Error(500, Title = "Server failed")]
            public static partial Error Failed { get; }
        }

        public static class Endpoints
        {
            public static void Map(IEndpointRouteBuilder app)
            {
                app.MapGet("/orders/{id:guid}", (Guid id) => Results.Ok(Find(id)));
                app.MapGet("/health", () => Results.Ok());
            }

            private static object Find(Guid id) => throw new OrderErrors.NotFoundException(id);
        }
        """;

    [Fact]
    public void The_fallback_is_documented_on_every_endpoint()
    {
        const string setup = """
            using ErrorApi.AspNetCore;

            namespace Shop;

            public static class Setup
            {
                public static void Configure(ErrorApiExceptionOptions o) => o.MapUnhandledException(ApiErrors.Failed);
            }
            """;

        var output = GeneratorHarness.RunAndCompile(Catalog, setup);

        // No EAPI010: the fallback counts as reachable everywhere.
        Assert.Empty(output.GeneratorDiagnostics);

        var metadata = output.Source("ErrorApi.Metadata.g.cs");
        Assert.Contains(
            "new global::ErrorApi.EndpointErrors(\"GET\", \"/health\", new global::ErrorApi.ErrorDescriptor[] { _errors[1] })",
            metadata,
            StringComparison.Ordinal);
        Assert.Contains(
            "new global::ErrorApi.EndpointErrors(\"GET\", \"/orders/{id}\", new global::ErrorApi.ErrorDescriptor[] { _errors[0], _errors[1] })",
            metadata,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_fallback_is_found_inside_the_generated_AddErrorApi_lambda()
    {
        // AddErrorApi(x => ...) is this generator's own output, so while it runs the call does not bind;
        // the argument still has to be found there, because that is where every application writes it.
        const string setup = """
            using ErrorApi.AspNetCore;
            using Microsoft.Extensions.DependencyInjection;

            namespace Shop;

            public static class Setup
            {
                public static void Configure(IServiceCollection services) =>
                    services.AddErrorApi(x => x.AddExceptionHandler(o => o.MapUnhandledException(ApiErrors.Failed)));
            }
            """;

        var output = GeneratorHarness.RunAndCompile(Catalog, setup);

        Assert.Empty(output.GeneratorDiagnostics);
        Assert.Contains(
            "new global::ErrorApi.EndpointErrors(\"GET\", \"/health\", new global::ErrorApi.ErrorDescriptor[] { _errors[1] })",
            output.Source("ErrorApi.Metadata.g.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_parameterless_form_documents_the_built_in_entry()
    {
        const string setup = """
            using ErrorApi.AspNetCore;

            namespace Shop;

            public static class Setup
            {
                public static void Configure(ErrorApiExceptionOptions o) => o.MapUnhandledException();
            }
            """;

        var output = GeneratorHarness.RunAndCompile(Catalog, setup);

        // Server.Failed is declared and still unreachable: the built-in entry is not it.
        Assert.Contains(output.GeneratorDiagnostics, d => d.Id == "EAPI010");

        // The document carries exactly what the runtime answers with — same code, status and title.
        var builtIn = ErrorApiExceptionOptions.DefaultUnhandledError;
        var metadata = output.Source("ErrorApi.Metadata.g.cs");
        Assert.Contains(
            $"new global::ErrorApi.ErrorDescriptor(\"{builtIn.Code}\", {builtIn.StatusCode}, \"{builtIn.Title}\"",
            metadata,
            StringComparison.Ordinal);
        Assert.Contains(
            "new global::ErrorApi.EndpointErrors(\"GET\", \"/health\", new global::ErrorApi.ErrorDescriptor[] { _errors[2] })",
            metadata,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_fallback_built_at_runtime_is_reported_as_undocumented()
    {
        const string setup = """
            using ErrorApi;
            using ErrorApi.AspNetCore;

            namespace Shop;

            public static class Setup
            {
                public static void Configure(ErrorApiExceptionOptions o) => o.MapUnhandledException(new Error("Server.Failed", 500));
            }
            """;

        var output = GeneratorHarness.RunAndCompile(Catalog, setup);

        Assert.Contains(output.GeneratorDiagnostics, d => d.Id == "EAPI014");
        // ...and the declared entry is still unreachable, because nothing tied the two together.
        Assert.Contains(output.GeneratorDiagnostics, d => d.Id == "EAPI010");
    }

    [Fact]
    public void A_same_named_method_on_another_type_is_not_the_fallback()
    {
        const string setup = """
            using ErrorApi;

            namespace Shop;

            public sealed class Unrelated
            {
                public void MapUnhandledException(Error error) { }

                public void Configure() => MapUnhandledException(ApiErrors.Failed);
            }
            """;

        var output = GeneratorHarness.RunAndCompile(Catalog, setup);

        Assert.DoesNotContain(output.GeneratorDiagnostics, d => d.Id == "EAPI014");
        Assert.Contains(output.GeneratorDiagnostics, d => d.Id == "EAPI010");
    }

    private sealed class OrderNotFoundException(Guid id) : Exception($"No order {id}.");

    private static readonly Error ServerFailed = new("Server.Failed", 500, "Server failed");

    private static ErrorApiExceptionHandler Handler(bool withFallback, FakeMetadata? metadata = null)
    {
        var options = new ErrorApiExceptionOptions { UseExceptionMessageAsDetail = true };
        if (withFallback)
        {
            options.MapUnhandledException(ServerFailed);
        }

        return new ErrorApiExceptionHandler(metadata ?? new FakeMetadata(), Options.Create(options));
    }

    private static HttpContext Context()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static JsonElement ReadBody(HttpContext context)
    {
        context.Response.Body.Position = 0;
        return JsonDocument.Parse(context.Response.Body).RootElement;
    }

    [Fact]
    public async Task An_unknown_exception_answers_with_the_fallback_and_never_its_message()
    {
        var context = Context();

        var handled = await Handler(withFallback: true)
            .TryHandleAsync(context, new InvalidOperationException("Server=db;Password=hunter2"), CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(500, context.Response.StatusCode);

        var body = ReadBody(context);
        Assert.Equal("Server.Failed", body.GetProperty("code").GetString());
        Assert.Equal("Server failed", body.GetProperty("title").GetString());
        Assert.False(body.TryGetProperty("detail", out _));
    }

    [Fact]
    public async Task The_catalog_still_answers_first()
    {
        var metadata = new FakeMetadata();
        metadata.ByType[typeof(OrderNotFoundException)] = FakeMetadata.NotFound;
        var context = Context();

        await Handler(withFallback: true, metadata)
            .TryHandleAsync(context, new OrderNotFoundException(Guid.Empty), CancellationToken.None);

        Assert.Equal(404, context.Response.StatusCode);
        Assert.Equal("Orders.NotFound", ReadBody(context).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Without_a_fallback_an_unknown_exception_is_still_left_alone()
    {
        var context = Context();

        var handled = await Handler(withFallback: false)
            .TryHandleAsync(context, new InvalidOperationException("boom"), CancellationToken.None);

        Assert.False(handled);
        Assert.Equal(0, context.Response.Body.Length);
    }

    [Fact]
    public void The_parameterless_form_uses_the_built_in_entry()
    {
        var options = new ErrorApiExceptionOptions().MapUnhandledException();

        Assert.Equal(ErrorApiExceptionOptions.DefaultUnhandledError, options.UnhandledError);
        Assert.Equal("Server.Unhandled", options.UnhandledError!.Value.Code);
        Assert.Equal(500, options.UnhandledError.Value.StatusCode);
    }

    [Fact]
    public void The_fallback_is_one_entry_not_a_chain()
    {
        var options = new ErrorApiExceptionOptions();

        Assert.Throws<ArgumentException>(() => options.MapUnhandledException(Error.None));

        options.MapUnhandledException(ServerFailed);
        Assert.Throws<InvalidOperationException>(() => options.MapUnhandledException(ServerFailed));
        Assert.Equal(ServerFailed, options.UnhandledError);
    }

    private sealed class AppHandler : IExceptionHandler
    {
        public ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken) =>
            ValueTask.FromResult(false);
    }

    [Fact]
    public void The_handler_registers_beside_one_the_application_already_has()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IExceptionHandler, AppHandler>();
        services.AddSingleton<IErrorApiMetadata>(new FakeMetadata());

        services.AddErrorApiExceptionHandler();

        var handlers = services.BuildServiceProvider().GetServices<IExceptionHandler>().ToList();
        Assert.Collection(
            handlers,
            first => Assert.IsType<AppHandler>(first),
            second => Assert.IsType<ErrorApiExceptionHandler>(second));
    }
}
