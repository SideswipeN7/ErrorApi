using ErrorApi;

namespace Sample.Api.Orders;

/// <summary>
/// The endpoints. Nothing here declares which errors can come back: the generator works that out by
/// following each handler into <see cref="IOrderService"/> and its implementation.
/// </summary>
public static class OrderEndpoints
{
    /// <summary>Maps the order feature under <c>/orders</c>.</summary>
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/orders").WithTags("Orders");

        orders.MapGet("/{id:guid}", GetById)
            .WithName("GetOrder")
            .WithSummary("Reads one order");

        // The location can only be built once the order exists, so it is a function of the value.
        // Pointing at the named route means the URL survives a change to the route template.
        orders.MapPost("/", (CreateOrderRequest request, IOrderService service) =>
                service.Create(request).ToCreatedAtRoute("GetOrder", order => new() { ["id"] = order.Id }))
            .WithName("CreateOrder")
            .WithSummary("Creates an order");

        orders.MapPost("/{id:guid}/pay", async (Guid id, PayOrderRequest request, IOrderService service, CancellationToken cancellationToken) =>
                (await service.Pay(id, request, cancellationToken)).ToHttpResult())
            .WithName("PayOrder")
            .WithSummary("Pays an order in full");

        orders.MapDelete("/{id:guid}", (Guid id, IOrderService service) => service.Cancel(id).ToHttpResult())
            .WithName("CancelOrder")
            .WithSummary("Cancels an order");

        // The quota check lives behind an interface the generator cannot follow, so the endpoint
        // declares that failure itself.
        orders.MapGet("/", [ProducesError("Common.RateLimited")] (IOrderService service) =>
                Results.Ok(Array.Empty<Order>()))
            .WithName("ListOrders")
            .WithSummary("Lists orders");

        return app;
    }

    private static IResult GetById(Guid id, IOrderService service) => service.GetById(id).ToHttpResult();
}
