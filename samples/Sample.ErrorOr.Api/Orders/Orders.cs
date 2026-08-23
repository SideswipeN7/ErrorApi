using System.Collections.Concurrent;
using ErrorOr;

namespace Sample.ErrorOr.Api.Orders;

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
/// The catalog, in ErrorOr's own error type.
/// </summary>
/// <remarks>
/// The attribute carries the status and nothing else. ErrorOr already writes the code as the
/// <c>code:</c> argument below, so the generator reads it from there — which means it cannot drift from
/// what the document promises, and there is no second copy to keep in step.
/// </remarks>
public static class OrderErrors
{
    [ErrorApi.Error(404, Description = "No order exists for the supplied identifier.")]
    public static Error NotFound => Error.NotFound("Orders.NotFound", "No such order.");

    [ErrorApi.Error(409, Description = "The order reached a terminal state before this request arrived.")]
    public static Error AlreadyPaid => Error.Conflict("Orders.AlreadyPaid", "Order was already paid.");

    [ErrorApi.Error(422)]
    public static Error AmountMismatch => Error.Validation("Orders.AmountMismatch", "Amount does not match the order total.");

    [ErrorApi.Error(400)]
    public static Error InvalidCustomer => Error.Validation("Orders.InvalidCustomer", "Customer must not be empty.");
}

/// <summary>
/// The application boundary the endpoints talk to. Endpoints only see the interface, which is exactly
/// the situation where a runtime error mapper cannot tell you what an endpoint returns.
/// </summary>
public interface IOrderService
{
    /// <summary>Reads one order.</summary>
    ErrorOr<Order> GetById(Guid id);

    /// <summary>Creates an order.</summary>
    ErrorOr<Order> Create(CreateOrderRequest request);

    /// <summary>Pays an order in full.</summary>
    ErrorOr<Success> Pay(Guid id, decimal amount);
}

/// <inheritdoc />
public sealed class InMemoryOrderService : IOrderService
{
    private readonly ConcurrentDictionary<Guid, Order> _orders = new();

    /// <inheritdoc />
    public ErrorOr<Order> GetById(Guid id) =>
        _orders.TryGetValue(id, out var order) ? order : OrderErrors.NotFound;

    /// <inheritdoc />
    public ErrorOr<Order> Create(CreateOrderRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Customer))
        {
            return OrderErrors.InvalidCustomer;
        }

        var order = new Order(Guid.NewGuid(), request.Customer, request.Total, OrderStatus.Pending);
        _orders[order.Id] = order;
        return order;
    }

    /// <inheritdoc />
    public ErrorOr<Success> Pay(Guid id, decimal amount)
    {
        // The 404 reaches the pay endpoint through this call, two frames below the handler.
        var lookup = GetById(id);
        if (lookup.IsError)
        {
            return lookup.FirstError;
        }

        var order = lookup.Value;
        if (order.Status == OrderStatus.Paid)
        {
            return OrderErrors.AlreadyPaid;
        }

        if (order.Total != amount)
        {
            return OrderErrors.AmountMismatch;
        }

        _orders[id] = order with { Status = OrderStatus.Paid };
        return Result.Success;
    }
}
