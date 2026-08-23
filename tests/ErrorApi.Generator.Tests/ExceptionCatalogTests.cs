using System.Text.Json;
using ErrorApi.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace ErrorApi.Generator.Tests;

/// <summary>
/// An exception type is a failure identified by a type, which is the shape the catalog already
/// understands. This is what lets a codebase that never adopted a result type still get a documented
/// contract, so the discovery half is pinned here alongside the response half.
/// </summary>
public sealed class ExceptionDiscoveryTests
{
    private const string Source = """
        using System;
        using ErrorApi;
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Http;
        using Microsoft.AspNetCore.Routing;

        namespace Shop;

        [ErrorCatalog("Orders")]
        public static class OrderErrors
        {
            [Error(404, Description = "No order exists for that id.")]
            public sealed class NotFoundException(Guid id) : Exception($"No order {id}.");

            [Error(409)]
            public sealed class AlreadyPaidException(Guid id) : Exception($"Order {id} was already paid.");
        }

        public sealed record Order(Guid Id);

        public interface IOrderService
        {
            Order GetById(Guid id);
            void Pay(Guid id);
        }

        public sealed class OrderService : IOrderService
        {
            public Order GetById(Guid id) =>
                id == Guid.Empty ? throw new OrderErrors.NotFoundException(id) : new Order(id);

            public void Pay(Guid id)
            {
                // The 404 reaches the pay endpoint through this call.
                var order = GetById(id);
                throw new OrderErrors.AlreadyPaidException(order.Id);
            }
        }

        public static class Endpoints
        {
            public static void Map(IEndpointRouteBuilder app)
            {
                app.MapGet("/orders/{id:guid}", (Guid id, IOrderService s) => Results.Ok(s.GetById(id)));
                app.MapPost("/orders/{id:guid}/pay", (Guid id, IOrderService s) => { s.Pay(id); return Results.NoContent(); });
            }
        }
        """;

    [Fact]
    public void A_thrown_exception_type_is_documented_on_the_endpoint_that_can_reach_it()
    {
        var metadata = GeneratorHarness.RunAndCompile(Source).Source("ErrorApi.Metadata.g.cs");

        Assert.Contains(
            "new global::ErrorApi.EndpointErrors(\"GET\", \"/orders/{id}\", new global::ErrorApi.ErrorDescriptor[] { _errors[1] })",
            metadata,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_throw_two_frames_below_the_handler_still_reaches_the_contract()
    {
        var metadata = GeneratorHarness.RunAndCompile(Source).Source("ErrorApi.Metadata.g.cs");

        // Pay throws AlreadyPaid itself and reaches NotFound through GetById.
        Assert.Contains(
            "new global::ErrorApi.EndpointErrors(\"POST\", \"/orders/{id}/pay\", new global::ErrorApi.ErrorDescriptor[] { _errors[0], _errors[1] })",
            metadata,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Exception_types_are_resolved_by_type_like_any_other_catalog_entry()
    {
        var metadata = GeneratorHarness.RunAndCompile(Source).Source("ErrorApi.Metadata.g.cs");

        Assert.Contains("global::Shop.OrderErrors.NotFoundException => _errors[1],", metadata, StringComparison.Ordinal);
        Assert.Contains("\"Orders.NotFound\", 404, \"Not found\"", metadata, StringComparison.Ordinal);
    }
}

public sealed class ExceptionHandlerTests
{
    private sealed class OrderNotFoundException(Guid id) : Exception($"No order {id}.");

    private static (ErrorApiExceptionHandler Handler, FakeMetadata Metadata) Build(bool useMessageAsDetail = true)
    {
        var metadata = new FakeMetadata();
        metadata.ByType[typeof(OrderNotFoundException)] = FakeMetadata.NotFound;

        var options = Options.Create(new ErrorApiExceptionOptions { UseExceptionMessageAsDetail = useMessageAsDetail });
        return (new ErrorApiExceptionHandler(metadata, options), metadata);
    }

    private static HttpContext Context()
    {
        // ProblemHttpResult resolves a logger factory, so the context needs a real container.
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
    public async Task An_annotated_exception_answers_with_the_documented_problem()
    {
        var (handler, _) = Build();
        var context = Context();

        var handled = await handler.TryHandleAsync(context, new OrderNotFoundException(Guid.Empty), CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(404, context.Response.StatusCode);

        var body = ReadBody(context);
        Assert.Equal("Orders.NotFound", body.GetProperty("code").GetString());
        Assert.Equal("Order not found", body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task The_message_becomes_the_detail_when_the_entry_has_none()
    {
        var (handler, _) = Build();
        var context = Context();

        await handler.TryHandleAsync(context, new OrderNotFoundException(Guid.Empty), CancellationToken.None);

        Assert.Equal(
            "No order 00000000-0000-0000-0000-000000000000.",
            ReadBody(context).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task The_message_can_be_kept_off_the_wire()
    {
        var (handler, _) = Build(useMessageAsDetail: false);
        var context = Context();

        await handler.TryHandleAsync(context, new OrderNotFoundException(Guid.Empty), CancellationToken.None);

        Assert.False(ReadBody(context).TryGetProperty("detail", out _));
    }

    [Fact]
    public async Task An_unknown_exception_is_left_for_whatever_handled_it_before()
    {
        var (handler, _) = Build();
        var context = Context();

        var handled = await handler.TryHandleAsync(context, new InvalidOperationException("boom"), CancellationToken.None);

        Assert.False(handled);
        Assert.Equal(0, context.Response.Body.Length);
    }

    [Fact]
    public void TryGetCatalogError_resolves_against_an_explicit_model()
    {
        var metadata = new FakeMetadata();
        metadata.ByType[typeof(OrderNotFoundException)] = FakeMetadata.AlreadyPaid;

        Assert.True(new OrderNotFoundException(Guid.Empty).TryGetCatalogError(out var error, metadata));
        Assert.Equal("Orders.AlreadyPaid", error.Code);

        Assert.False(new InvalidOperationException().TryGetCatalogError(out _, metadata));
    }
}
