using System.Collections.Concurrent;
using ErrorApi;

namespace Sample.Exceptions.Api.Orders;

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
/// The catalog, as plain exceptions.
/// </summary>
/// <remarks>
/// No result type anywhere in this sample. The service throws, the endpoints call it, and the generator
/// documents each endpoint from the <c>throw new</c> it can reach — which is the shape most existing
/// codebases are already in. The trailing <c>Exception</c> is dropped from the inferred code, because a
/// client does not care how the server models the failure: <c>NotFoundException</c> becomes
/// <c>Orders.NotFound</c>.
/// </remarks>
[ErrorCatalog("Orders")]
public static class OrderErrors
{
    [Error(404, Description = "No order exists for the supplied identifier.")]
    public sealed class NotFoundException(Guid id) : Exception($"No order {id}.");

    [Error(409, Description = "The order reached a terminal state before this request arrived.")]
    public sealed class AlreadyPaidException(Guid id) : Exception($"Order {id} was already paid.");

    [Error(422)]
    public sealed class AmountMismatchException(decimal expected, decimal actual)
        : Exception($"Expected {expected}, received {actual}.");

    [Error(400)]
    public sealed class InvalidCustomerException() : Exception("Customer must not be empty.");
}

/// <summary>
/// The application boundary the endpoints talk to. Endpoints only see the interface, which is exactly
/// the situation where a runtime error mapper cannot tell you what an endpoint returns.
/// </summary>
public interface IOrderService
{
    /// <summary>Reads one order.</summary>
    Order GetById(Guid id);

    /// <summary>Creates an order.</summary>
    Order Create(CreateOrderRequest request);

    /// <summary>Pays an order in full.</summary>
    void Pay(Guid id, decimal amount);
}

/// <inheritdoc />
public sealed class InMemoryOrderService : IOrderService
{
    private readonly ConcurrentDictionary<Guid, Order> _orders = new();

    /// <inheritdoc />
    public Order GetById(Guid id) =>
        _orders.TryGetValue(id, out var order) ? order : throw new OrderErrors.NotFoundException(id);

    /// <inheritdoc />
    public Order Create(CreateOrderRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Customer))
        {
            throw new OrderErrors.InvalidCustomerException();
        }

        var order = new Order(Guid.NewGuid(), request.Customer, request.Total, OrderStatus.Pending);
        _orders[order.Id] = order;
        return order;
    }

    /// <inheritdoc />
    public void Pay(Guid id, decimal amount)
    {
        // The 404 reaches the pay endpoint through this call, two frames below the handler.
        var order = GetById(id);

        if (order.Status == OrderStatus.Paid)
        {
            throw new OrderErrors.AlreadyPaidException(id);
        }

        if (order.Total != amount)
        {
            throw new OrderErrors.AmountMismatchException(order.Total, amount);
        }

        _orders[id] = order with { Status = OrderStatus.Paid };
    }
}
