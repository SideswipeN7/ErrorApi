using System.Collections.Concurrent;
using ErrorApi;

namespace Sample.Wolverine.Api.Orders;

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

/// <summary>The catalog. Nothing about it is Wolverine-specific.</summary>
[ErrorCatalog("Orders")]
public static partial class OrderErrors
{
    [Error(StatusCodes.Status404NotFound, Description = "No order exists for the supplied identifier.")]
    public static partial Error NotFound { get; }

    [Error(StatusCodes.Status409Conflict, Description = "The order reached a terminal state before this request arrived.")]
    public static partial Error AlreadyPaid { get; }

    [Error(StatusCodes.Status422UnprocessableEntity, Detail = "Expected {0}, received {1}.")]
    public static partial Error AmountMismatch(decimal expected, decimal actual);

    [Error(StatusCodes.Status400BadRequest, Title = "Customer must not be empty")]
    public static partial Error InvalidCustomer { get; }
}

/// <summary>The in-memory store the handlers share.</summary>
public sealed class OrderStore
{
    private readonly ConcurrentDictionary<Guid, Order> _orders = new();

    /// <summary>Reads one order, or the catalog entry saying there is none.</summary>
    public Result<Order> Find(Guid id) =>
        _orders.TryGetValue(id, out var order) ? order : OrderErrors.NotFound;

    /// <summary>Stores an order.</summary>
    public void Save(Order order) => _orders[order.Id] = order;
}

/// <summary>Reads one order.</summary>
public sealed record GetOrder(Guid Id);

/// <summary>Creates an order.</summary>
public sealed record CreateOrder(string Customer, decimal Total);

/// <summary>Pays an order in full.</summary>
public sealed record PayOrder(Guid Id, decimal Amount);

/// <summary>
/// The Wolverine shape: no interface anywhere. Wolverine matches these by convention — a class named
/// <c>*Handler</c> with a <c>Handle</c> method whose first parameter is the message — and so does the
/// generator, which is how the failures below reach the endpoint contracts through
/// <c>IMessageBus.InvokeAsync</c>.
/// </summary>
public sealed class GetOrderHandler
{
    /// <inheritdoc cref="GetOrder"/>
    public Result<Order> Handle(GetOrder query, OrderStore store) => store.Find(query.Id);
}

/// <inheritdoc cref="CreateOrder"/>
public sealed class CreateOrderHandler
{
    /// <inheritdoc cref="CreateOrder"/>
    public Result<Order> Handle(CreateOrder command, OrderStore store)
    {
        if (string.IsNullOrWhiteSpace(command.Customer))
        {
            return OrderErrors.InvalidCustomer;
        }

        var order = new Order(Guid.NewGuid(), command.Customer, command.Total, OrderStatus.Pending);
        store.Save(order);
        return order;
    }
}

/// <inheritdoc cref="PayOrder"/>
public sealed class PayOrderHandler
{
    /// <inheritdoc cref="PayOrder"/>
    public Result Handle(PayOrder command, OrderStore store)
    {
        // The 404 reaches the pay endpoint from here, through the store, behind the message bus.
        var lookup = store.Find(command.Id);
        if (lookup.IsFailure)
        {
            return Result.Failure(lookup.Error);
        }

        var order = lookup.Value;
        if (order.Status == OrderStatus.Paid)
        {
            return Result.Failure(OrderErrors.AlreadyPaid);
        }

        if (order.Total != command.Amount)
        {
            return Result.Failure(OrderErrors.AmountMismatch(order.Total, command.Amount));
        }

        store.Save(order with { Status = OrderStatus.Paid });
        return Result.Success();
    }
}
