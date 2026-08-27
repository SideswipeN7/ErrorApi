using System.Collections.Concurrent;
using FluentResults;

// FluentResults and ErrorApi both export Result, so there is no `using ErrorApi;` in this file:
// importing both would make every mention of Result ambiguous. FluentResults keeps the plain name.
using FluentError = FluentResults.Error;

namespace Sample.FluentResultsApi.Orders;

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
/// The catalog, as FluentResults error subclasses.
/// </summary>
/// <remarks>
/// <c>Result.Fail("message")</c> carries neither a code nor a status, so a message alone can never be a
/// contract. Modelling each failure as its own <c>Error</c> subclass is what FluentResults recommends
/// anyway, and a type is a shape the catalog already understands — the attribute only adds the HTTP half.
/// </remarks>
[ErrorApi.ErrorCatalog("Orders")]
public static class OrderErrors
{
    [ErrorApi.Error(404, Description = "No order exists for the supplied identifier.")]
    public sealed class NotFound(Guid id) : FluentError($"No order {id}.");

    [ErrorApi.Error(409, Description = "The order reached a terminal state before this request arrived.")]
    public sealed class AlreadyPaid(Guid id) : FluentError($"Order {id} was already paid.");

    [ErrorApi.Error(422)]
    public sealed class AmountMismatch(decimal expected, decimal actual)
        : FluentError($"Expected {expected}, received {actual}.");

    [ErrorApi.Error(400)]
    public sealed class InvalidCustomer() : FluentError("Customer must not be empty.");

    [ErrorApi.Error(400)]
    public sealed class InvalidTotal() : FluentError("Total must be greater than zero.");
}

/// <summary>
/// The application boundary the endpoints talk to. Endpoints only see the interface, which is exactly
/// the situation where a runtime error mapper cannot tell you what an endpoint returns.
/// </summary>
public interface IOrderService
{
    /// <summary>Reads one order.</summary>
    Result<Order> GetById(Guid id);

    /// <summary>Creates an order.</summary>
    Result<Order> Create(CreateOrderRequest request);

    /// <summary>Pays an order in full.</summary>
    Result Pay(Guid id, decimal amount);
}

/// <inheritdoc />
public sealed class InMemoryOrderService : IOrderService
{
    private readonly ConcurrentDictionary<Guid, Order> _orders = new();

    /// <inheritdoc />
    public Result<Order> GetById(Guid id) =>
        _orders.TryGetValue(id, out var order)
            ? Result.Ok(order)
            : Result.Fail(new OrderErrors.NotFound(id));

    /// <inheritdoc />
    public Result<Order> Create(CreateOrderRequest request)
    {
        // Validation accumulates, which is FluentResults' home turf. Both failures are 400s, so the
        // documented statuses do not change — the first error answers, the rest ride along in the
        // `errors` member when IncludeAllErrors is on.
        var failures = new List<global::FluentResults.IError>();

        if (string.IsNullOrWhiteSpace(request.Customer))
        {
            failures.Add(new OrderErrors.InvalidCustomer());
        }

        if (request.Total <= 0)
        {
            failures.Add(new OrderErrors.InvalidTotal());
        }

        if (failures.Count > 0)
        {
            return Result.Fail<Order>(failures);
        }

        var order = new Order(Guid.NewGuid(), request.Customer, request.Total, OrderStatus.Pending);
        _orders[order.Id] = order;
        return Result.Ok(order);
    }

    /// <inheritdoc />
    public Result Pay(Guid id, decimal amount)
    {
        // The 404 reaches the pay endpoint through this call, two frames below the handler.
        var lookup = GetById(id);
        if (lookup.IsFailed)
        {
            return lookup.ToResult();
        }

        var order = lookup.Value;
        if (order.Status == OrderStatus.Paid)
        {
            return Result.Fail(new OrderErrors.AlreadyPaid(id));
        }

        if (order.Total != amount)
        {
            return Result.Fail(new OrderErrors.AmountMismatch(order.Total, amount));
        }

        _orders[id] = order with { Status = OrderStatus.Paid };
        return Result.Ok();
    }
}
