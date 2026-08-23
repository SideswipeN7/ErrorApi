using System.Collections.Concurrent;
using ErrorApi;
using OneOf;

namespace Sample.OneOf.Api.Orders;

/// <summary>An order as returned to clients.</summary>
public sealed record Order(Guid Id, string Customer, decimal Total, OrderStatus Status);

/// <summary>Lifecycle of an order.</summary>
public enum OrderStatus
{
    /// <summary>Created, not paid yet.</summary>
    Pending,

    /// <summary>Paid in full.</summary>
    Paid,
}

/// <summary>Body of <c>POST /orders</c>.</summary>
public sealed record CreateOrderRequest(string Customer, decimal Total);

/// <summary>
/// The catalog. With a union the failure <em>is</em> a type, so that is where the attribute goes.
/// </summary>
/// <remarks>
/// Nesting the cases inside a catalog type keeps the names short at the use site — <c>OneOf&lt;Order,
/// OrderErrors.NotFound&gt;</c> — while <c>[ErrorCatalog]</c> gives them the dotted wire codes a client
/// wants: <c>Orders.NotFound</c>. Each case carries whatever the failure needs to explain itself.
/// </remarks>
[ErrorCatalog("Orders")]
public static class OrderErrors
{
    [Error(404, Description = "No order exists for the supplied identifier.")]
    public sealed record NotFound(Guid Id);

    [Error(409, Description = "The order reached a terminal state before this request arrived.")]
    public sealed record AlreadyPaid(Guid Id);

    [Error(422)]
    public sealed record AmountMismatch(decimal Expected, decimal Actual);

    [Error(400)]
    public sealed record InvalidCustomer;
}

/// <summary>
/// The application boundary the endpoints talk to. Endpoints only see the interface, which is exactly
/// the situation where a runtime error mapper cannot tell you what an endpoint returns.
/// </summary>
public interface IOrderService
{
    /// <summary>Reads one order.</summary>
    OneOf<Order, OrderErrors.NotFound> GetById(Guid id);

    /// <summary>Creates an order.</summary>
    OneOf<Order, OrderErrors.InvalidCustomer> Create(CreateOrderRequest request);

    /// <summary>Pays an order in full.</summary>
    OneOf<Order, OrderErrors.NotFound, OrderErrors.AlreadyPaid, OrderErrors.AmountMismatch> Pay(Guid id, decimal amount);
}

/// <inheritdoc />
public sealed class InMemoryOrderService : IOrderService
{
    private readonly ConcurrentDictionary<Guid, Order> _orders = new();

    /// <inheritdoc />
    public OneOf<Order, OrderErrors.NotFound> GetById(Guid id) =>
        _orders.TryGetValue(id, out var order) ? order : new OrderErrors.NotFound(id);

    /// <inheritdoc />
    public OneOf<Order, OrderErrors.InvalidCustomer> Create(CreateOrderRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Customer))
        {
            return new OrderErrors.InvalidCustomer();
        }

        var order = new Order(Guid.NewGuid(), request.Customer, request.Total, OrderStatus.Pending);
        _orders[order.Id] = order;
        return order;
    }

    /// <inheritdoc />
    public OneOf<Order, OrderErrors.NotFound, OrderErrors.AlreadyPaid, OrderErrors.AmountMismatch> Pay(Guid id, decimal amount)
    {
        if (!_orders.TryGetValue(id, out var order))
        {
            return new OrderErrors.NotFound(id);
        }

        if (order.Status == OrderStatus.Paid)
        {
            return new OrderErrors.AlreadyPaid(id);
        }

        if (order.Total != amount)
        {
            return new OrderErrors.AmountMismatch(order.Total, amount);
        }

        var paid = order with { Status = OrderStatus.Paid };
        _orders[id] = paid;
        return paid;
    }
}
