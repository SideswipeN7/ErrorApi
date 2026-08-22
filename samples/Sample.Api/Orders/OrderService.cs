using System.Collections.Concurrent;
using ErrorApi;

namespace Sample.Api.Orders;

/// <summary>An order as returned to clients.</summary>
public sealed record Order(Guid Id, string Customer, decimal Total, string Currency, OrderStatus Status);

/// <summary>Lifecycle of an order.</summary>
public enum OrderStatus
{
    /// <summary>Created, not paid yet.</summary>
    Pending,

    /// <summary>Paid in full.</summary>
    Paid,

    /// <summary>Cancelled by the customer.</summary>
    Cancelled,
}

/// <summary>Body of <c>POST /orders</c>.</summary>
public sealed record CreateOrderRequest(string Customer, decimal Total);

/// <summary>Body of <c>POST /orders/{id}/pay</c>.</summary>
public sealed record PayOrderRequest(decimal Amount, string Currency);

/// <summary>
/// The application boundary the endpoints talk to. Endpoints only see the interface, which is exactly
/// the situation where a runtime error mapper cannot tell you what an endpoint returns: the generator
/// follows the dispatch into the implementation below.
/// </summary>
public interface IOrderService
{
    /// <summary>Reads one order.</summary>
    Result<Order> GetById(Guid id);

    /// <summary>Creates an order.</summary>
    Result<Order> Create(CreateOrderRequest request);

    /// <summary>Pays an order in full.</summary>
    Task<Result> Pay(Guid id, PayOrderRequest request, CancellationToken cancellationToken);

    /// <summary>Cancels an order.</summary>
    Result Cancel(Guid id);
}

/// <inheritdoc />
public sealed class InMemoryOrderService : IOrderService
{
    private readonly ConcurrentDictionary<Guid, Order> _orders = new();

    /// <inheritdoc />
    public Result<Order> GetById(Guid id) =>
        _orders.TryGetValue(id, out var order)
            ? order
            : OrderErrors.NotFound;

    /// <inheritdoc />
    public Result<Order> Create(CreateOrderRequest request)
    {
        if (Validate(request) is { IsFailure: true } invalid)
        {
            return invalid.Error;
        }

        var order = new Order(Guid.NewGuid(), request.Customer, request.Total, "PLN", OrderStatus.Pending);
        _orders[order.Id] = order;
        return order;
    }

    /// <inheritdoc />
    public Task<Result> Pay(Guid id, PayOrderRequest request, CancellationToken cancellationToken)
    {
        var lookup = GetById(id);
        if (lookup.IsFailure)
        {
            return Task.FromResult(Result.Failure(lookup.Error));
        }

        var order = lookup.Value;
        if (order.Status == OrderStatus.Cancelled)
        {
            return Task.FromResult(Result.Failure(OrderErrors.Cancelled));
        }

        if (order.Status == OrderStatus.Paid)
        {
            return Task.FromResult(Result.Failure(OrderErrors.AlreadyPaid(id)));
        }

        if (!string.Equals(order.Currency, request.Currency, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(Result.Failure(OrderErrors.CurrencyMismatch(order.Currency, request.Currency)));
        }

        if (order.Total != request.Amount)
        {
            return Task.FromResult(Result.Failure(OrderErrors.AmountMismatch(order.Total, request.Amount)));
        }

        _orders[id] = order with { Status = OrderStatus.Paid };
        return Task.FromResult(Result.Success());
    }

    /// <inheritdoc />
    public Result Cancel(Guid id)
    {
        var lookup = GetById(id);
        if (lookup.IsFailure)
        {
            return lookup.Error;
        }

        if (lookup.Value.Status == OrderStatus.Paid)
        {
            return OrderErrors.AlreadyPaid(id);
        }

        _orders[id] = lookup.Value with { Status = OrderStatus.Cancelled };
        return Result.Success();
    }

    private static Result Validate(CreateOrderRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Customer))
        {
            return CommonErrors.Validation("customer must not be empty");
        }

        return request.Total <= 0
            ? CommonErrors.Validation("total must be greater than zero")
            : Result.Success();
    }
}
