using System.Collections.Concurrent;
using ErrorApi;
using MediatR;

namespace Sample.Mediator.Api.Orders;

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

/// <summary>The catalog. Nothing about it is mediator-specific.</summary>
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
public sealed record GetOrder(Guid Id) : IRequest<Result<Order>>;

/// <summary>Creates an order.</summary>
public sealed record CreateOrder(string Customer, decimal Total) : IRequest<Result<Order>>;

/// <summary>Pays an order in full.</summary>
public sealed record PayOrder(Guid Id, decimal Amount) : IRequest<Result>;

/// <inheritdoc cref="GetOrder"/>
public sealed class GetOrderHandler(OrderStore store) : IRequestHandler<GetOrder, Result<Order>>
{
    /// <inheritdoc />
    public Task<Result<Order>> Handle(GetOrder request, CancellationToken cancellationToken) =>
        Task.FromResult(store.Find(request.Id));
}

/// <inheritdoc cref="CreateOrder"/>
public sealed class CreateOrderHandler(OrderStore store) : IRequestHandler<CreateOrder, Result<Order>>
{
    /// <inheritdoc />
    public Task<Result<Order>> Handle(CreateOrder request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Customer))
        {
            return Task.FromResult<Result<Order>>(OrderErrors.InvalidCustomer);
        }

        var order = new Order(Guid.NewGuid(), request.Customer, request.Total, OrderStatus.Pending);
        store.Save(order);
        return Task.FromResult<Result<Order>>(order);
    }
}

/// <inheritdoc cref="PayOrder"/>
public sealed class PayOrderHandler(OrderStore store) : IRequestHandler<PayOrder, Result>
{
    /// <inheritdoc />
    public Task<Result> Handle(PayOrder request, CancellationToken cancellationToken)
    {
        // The 404 reaches the pay endpoint from here, through the store, behind the mediator.
        var lookup = store.Find(request.Id);
        if (lookup.IsFailure)
        {
            return Task.FromResult(Result.Failure(lookup.Error));
        }

        var order = lookup.Value;
        if (order.Status == OrderStatus.Paid)
        {
            return Task.FromResult(Result.Failure(OrderErrors.AlreadyPaid));
        }

        if (order.Total != request.Amount)
        {
            return Task.FromResult(Result.Failure(OrderErrors.AmountMismatch(order.Total, request.Amount)));
        }

        store.Save(order with { Status = OrderStatus.Paid });
        return Task.FromResult(Result.Success());
    }
}
