using System.Collections.Concurrent;
using CSharpFunctionalExtensions;

namespace Sample.Cfe.Api.Orders;

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

/// <summary>Creates an order.</summary>
public sealed record CreateOrder(string Customer, decimal Total);

/// <summary>
/// The failure side of <c>Result&lt;T, OrderError&gt;</c>: a closed hierarchy of the API's own types.
/// With CSharpFunctionalExtensions the error <em>is</em> a type, so that is where the catalog entry
/// goes — each concrete case carries its <c>[Error]</c>, and the generator documents the endpoints
/// wherever it sees a case constructed.
/// </summary>
public abstract record OrderError;

/// <summary>No order exists for the supplied identifier.</summary>
[ErrorApi.Error("Orders.NotFound", StatusCodes.Status404NotFound, Title = "Order not found")]
public sealed record OrderNotFound(Guid Id) : OrderError;

/// <summary>The order reached a terminal state before this request arrived.</summary>
[ErrorApi.Error("Orders.AlreadyPaid", StatusCodes.Status409Conflict, Title = "Order already paid")]
public sealed record OrderAlreadyPaid(Guid Id) : OrderError;

/// <summary>The paid amount does not match the order total.</summary>
[ErrorApi.Error("Orders.AmountMismatch", StatusCodes.Status422UnprocessableEntity)]
public sealed record OrderAmountMismatch(decimal Expected, decimal Actual) : OrderError;

/// <summary>The customer name failed validation.</summary>
[ErrorApi.Error("Orders.InvalidCustomer", StatusCodes.Status400BadRequest, Title = "Customer must not be empty")]
public sealed record OrderInvalidCustomer : OrderError;

/// <summary>The application boundary the endpoints talk to.</summary>
public interface IOrderService
{
    /// <summary>Reads one order, or says why it cannot.</summary>
    Result<Order, OrderError> GetById(Guid id);

    /// <summary>Creates an order, or says why it cannot.</summary>
    Result<Order, OrderError> Create(CreateOrder request);

    /// <summary>Pays an order in full, or says why it cannot.</summary>
    UnitResult<OrderError> Pay(Guid id, decimal amount);
}

/// <inheritdoc />
public sealed class InMemoryOrderService : IOrderService
{
    private readonly ConcurrentDictionary<Guid, Order> _orders = new();

    /// <inheritdoc />
    public Result<Order, OrderError> GetById(Guid id) =>
        _orders.TryGetValue(id, out var order)
            ? order
            : new OrderNotFound(id);

    /// <inheritdoc />
    public Result<Order, OrderError> Create(CreateOrder request)
    {
        if (string.IsNullOrWhiteSpace(request.Customer))
        {
            return new OrderInvalidCustomer();
        }

        var order = new Order(Guid.NewGuid(), request.Customer, request.Total, OrderStatus.Pending);
        _orders[order.Id] = order;
        return order;
    }

    /// <inheritdoc />
    public UnitResult<OrderError> Pay(Guid id, decimal amount)
    {
        var lookup = GetById(id);
        if (lookup.IsFailure)
        {
            return UnitResult.Failure(lookup.Error);
        }

        var order = lookup.Value;
        if (order.Status == OrderStatus.Paid)
        {
            return UnitResult.Failure<OrderError>(new OrderAlreadyPaid(id));
        }

        if (order.Total != amount)
        {
            return UnitResult.Failure<OrderError>(new OrderAmountMismatch(order.Total, amount));
        }

        _orders[id] = order with { Status = OrderStatus.Paid };
        return UnitResult.Success<OrderError>();
    }
}
