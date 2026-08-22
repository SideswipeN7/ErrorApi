namespace ErrorApi.Generator.Tests;

/// <summary>Snippets the tests compile. Kept together so a change to the shape shows up in one place.</summary>
public static class TestSources
{
    /// <summary>A catalog with a plain entry, a templated entry, and a second catalog class.</summary>
    public const string Catalog = """
        using System;
        using ErrorApi;

        namespace Shop.Orders;

        public static partial class OrderErrors
        {
            [Error("Orders.NotFound", 404, Title = "Order not found", Description = "No order exists for that id.")]
            public static partial Error NotFound { get; }

            [Error("Orders.AlreadyPaid", 409, Title = "Order already paid", Detail = "Order {0} was already paid.")]
            public static partial Error AlreadyPaid(Guid orderId);
        }

        public static partial class BillingErrors
        {
            [Error("Billing.CardDeclined", 402, Title = "Card declined")]
            public static partial Error CardDeclined { get; }
        }
        """;

    /// <summary>A service behind an interface, which is what the walker has to see through.</summary>
    public const string Service = """
        using System;
        using ErrorApi;

        namespace Shop.Orders;

        public sealed record Order(Guid Id, decimal Total);

        public interface IOrderService
        {
            Result<Order> GetById(Guid id);
            Result Pay(Guid id);
        }

        public sealed class OrderService : IOrderService
        {
            public Result<Order> GetById(Guid id) => OrderErrors.NotFound;

            public Result Pay(Guid id)
            {
                var order = GetById(id);
                if (order.IsFailure)
                {
                    return order.Error;
                }

                return Charge(order.Value);
            }

            private static Result Charge(Order order) =>
                order.Total > 0 ? BillingErrors.CardDeclined : Result.Success();
        }
        """;

    /// <summary>Endpoints mapped through a group, using both a lambda and a method group.</summary>
    public const string Endpoints = """
        using System;
        using ErrorApi;
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Http;
        using Microsoft.AspNetCore.Routing;

        namespace Shop.Orders;

        public static class OrderEndpoints
        {
            public static IEndpointRouteBuilder MapOrders(this IEndpointRouteBuilder app)
            {
                var orders = app.MapGroup("/orders").WithTags("Orders");

                orders.MapGet("/{id:guid}", GetById);
                orders.MapPost("/{id:guid}/pay", (Guid id, IOrderService service) => service.Pay(id).ToHttpResult());
                orders.MapGet("/", [ProducesError("Orders.NotFound")] () => Results.Ok());

                return app;
            }

            private static IResult GetById(Guid id, IOrderService service) => service.GetById(id).ToHttpResult();
        }
        """;
}
