using System.Collections.Concurrent;
using ErrorApi;

using ArdalisResult = Ardalis.Result.Result;
using ValidationError = Ardalis.Result.ValidationError;

namespace Sample.Ardalis.Api.Orders;

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
/// The catalog, Ardalis style: factory members. Ardalis.Result has no typed error and no code slot of
/// its own, so each factory carries the code where Ardalis has room for it — an error message, or
/// <c>ValidationError.ErrorCode</c> — and the <c>[Error]</c> attribute is what ties that code to a
/// status and title the document can promise.
/// </summary>
[ErrorCatalog("Orders")]
public static class OrderErrors
{
    /// <summary>No order exists for the supplied identifier.</summary>
    [ErrorApi.Error("Orders.NotFound", StatusCodes.Status404NotFound, Title = "Order not found")]
    public static ArdalisResult NotFound() => ArdalisResult.NotFound("Orders.NotFound");

    /// <summary>The order reached a terminal state before this request arrived.</summary>
    [ErrorApi.Error("Orders.AlreadyPaid", StatusCodes.Status409Conflict, Title = "Order already paid")]
    public static ArdalisResult AlreadyPaid() => ArdalisResult.Conflict("Orders.AlreadyPaid");

    /// <summary>The paid amount does not match the order total.</summary>
    [ErrorApi.Error("Orders.AmountMismatch", StatusCodes.Status422UnprocessableEntity)]
    public static ArdalisResult AmountMismatch() => ArdalisResult.Error("Orders.AmountMismatch");

    /// <summary>The customer name failed validation.</summary>
    [ErrorApi.Error("Orders.InvalidCustomer", StatusCodes.Status400BadRequest, Title = "Customer must not be empty")]
    public static ArdalisResult InvalidCustomer() => ArdalisResult.Invalid(new ValidationError
    {
        Identifier = nameof(CreateOrder.Customer),
        ErrorCode = "Orders.InvalidCustomer",
        ErrorMessage = "Customer must not be empty.",
    });
}

/// <summary>The application boundary the endpoints talk to.</summary>
public interface IOrderService
{
    /// <summary>Reads one order, or says why it cannot.</summary>
    global::Ardalis.Result.Result<Order> GetById(Guid id);

    /// <summary>Creates an order, or says why it cannot.</summary>
    global::Ardalis.Result.Result<Order> Create(CreateOrder request);

    /// <summary>Pays an order in full, or says why it cannot.</summary>
    ArdalisResult Pay(Guid id, decimal amount);
}

/// <inheritdoc />
public sealed class InMemoryOrderService : IOrderService
{
    private readonly ConcurrentDictionary<Guid, Order> _orders = new();

    /// <inheritdoc />
    public global::Ardalis.Result.Result<Order> GetById(Guid id) =>
        _orders.TryGetValue(id, out var order) ? order : OrderErrors.NotFound();

    /// <inheritdoc />
    public global::Ardalis.Result.Result<Order> Create(CreateOrder request)
    {
        if (string.IsNullOrWhiteSpace(request.Customer))
        {
            return OrderErrors.InvalidCustomer();
        }

        var order = new Order(Guid.NewGuid(), request.Customer, request.Total, OrderStatus.Pending);
        _orders[order.Id] = order;
        return order;
    }

    /// <inheritdoc />
    public ArdalisResult Pay(Guid id, decimal amount)
    {
        var lookup = GetById(id);
        if (!lookup.IsSuccess)
        {
            return OrderErrors.NotFound();
        }

        var order = lookup.Value;
        if (order.Status == OrderStatus.Paid)
        {
            return OrderErrors.AlreadyPaid();
        }

        if (order.Total != amount)
        {
            return OrderErrors.AmountMismatch();
        }

        _orders[id] = order with { Status = OrderStatus.Paid };
        return ArdalisResult.Success();
    }
}
