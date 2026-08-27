using System.Collections.Concurrent;
using ErrorApi;

namespace Sample.Controllers.Api.Orders;

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

/// <summary>The catalog. Nothing about it is controller-specific.</summary>
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

/// <summary>The in-memory store the controller talks to through an interface.</summary>
public interface IOrderStore
{
    /// <summary>Reads one order, or the catalog entry saying there is none.</summary>
    Result<Order> Find(Guid id);

    /// <summary>Creates an order, or says why it cannot.</summary>
    Result<Order> Create(CreateOrder command);

    /// <summary>Pays an order in full, or says why it cannot.</summary>
    Result Pay(Guid id, decimal amount);
}

/// <inheritdoc />
public sealed class InMemoryOrderStore : IOrderStore
{
    private readonly ConcurrentDictionary<Guid, Order> _orders = new();

    /// <inheritdoc />
    public Result<Order> Find(Guid id) =>
        _orders.TryGetValue(id, out var order) ? order : OrderErrors.NotFound;

    /// <inheritdoc />
    public Result<Order> Create(CreateOrder command)
    {
        if (string.IsNullOrWhiteSpace(command.Customer))
        {
            return OrderErrors.InvalidCustomer;
        }

        var order = new Order(Guid.NewGuid(), command.Customer, command.Total, OrderStatus.Pending);
        _orders[order.Id] = order;
        return order;
    }

    /// <inheritdoc />
    public Result Pay(Guid id, decimal amount)
    {
        var lookup = Find(id);
        if (lookup.IsFailure)
        {
            return Result.Failure(lookup.Error);
        }

        var order = lookup.Value;
        if (order.Status == OrderStatus.Paid)
        {
            return Result.Failure(OrderErrors.AlreadyPaid);
        }

        if (order.Total != amount)
        {
            return Result.Failure(OrderErrors.AmountMismatch(order.Total, amount));
        }

        _orders[id] = order with { Status = OrderStatus.Paid };
        return Result.Success();
    }
}
