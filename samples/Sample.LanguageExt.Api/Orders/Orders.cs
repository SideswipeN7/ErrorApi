using System.Collections.Concurrent;
using ErrorApi;
using LanguageExt;
using LanguageExt.Common;

// language-ext and ErrorApi both export a type called Error. The attribute still binds to ours,
// because only ours is an attribute, but a bare Error in an expression has to be spelled out.
using LangError = LanguageExt.Common.Error;

namespace Sample.LanguageExt.Api.Orders;

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
/// The catalog, as <c>Expected</c> subclasses — the idiomatic way to model a domain failure in
/// language-ext.
/// </summary>
/// <remarks>
/// Each <c>Expected</c> already carries the message and the status, so a bare <c>[Error]</c> is all it
/// takes: the status and title are read from the base constructor call, the wire code from the name
/// under the catalog's prefix — <c>Orders.NotFound</c>. <c>[ErrorDescription]</c> adds documentation
/// prose where it earns its line; nothing is written twice.
/// </remarks>
[ErrorCatalog("Orders")]
public static class OrderErrors
{
    [Error, ErrorDescription("No order exists for the supplied identifier.")]
    public sealed record NotFound(Guid Id) : Expected("Order not found", 404);

    [Error, ErrorDescription("The order reached a terminal state before this request arrived.")]
    public sealed record AlreadyPaid(Guid Id) : Expected("Order was already paid", 409);

    // The positional parameter is not called Expected on purpose: it would shadow the base type, and
    // the project namespace already shadows LanguageExt, so the qualified name would not save it.
    [Error]
    public sealed record AmountMismatch(decimal ExpectedTotal, decimal Actual)
        : Expected("Amount does not match the order total", 422);

    [Error]
    public sealed record InvalidCustomer() : Expected("Customer must not be empty", 400);
}

/// <summary>
/// The application boundary the endpoints talk to. Endpoints only see the interface, which is exactly
/// the situation where a runtime error mapper cannot tell you what an endpoint returns.
/// </summary>
public interface IOrderService
{
    /// <summary>Reads one order.</summary>
    Fin<Order> GetById(Guid id);

    /// <summary>Creates an order.</summary>
    Fin<Order> Create(CreateOrderRequest request);

    /// <summary>Pays an order in full.</summary>
    Fin<Unit> Pay(Guid id, decimal amount);
}

/// <inheritdoc />
public sealed class InMemoryOrderService : IOrderService
{
    private readonly ConcurrentDictionary<Guid, Order> _orders = new();

    /// <inheritdoc />
    public Fin<Order> GetById(Guid id) =>
        _orders.TryGetValue(id, out var order) ? order : new OrderErrors.NotFound(id);

    /// <inheritdoc />
    public Fin<Order> Create(CreateOrderRequest request)
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
    public Fin<Unit> Pay(Guid id, decimal amount)
    {
        // The 404 reaches the pay endpoint through this call, two frames below the handler.
        var lookup = GetById(id);
        if (lookup.IsFail)
        {
            return (LangError)lookup;
        }

        var order = (Order)lookup;
        if (order.Status == OrderStatus.Paid)
        {
            return new OrderErrors.AlreadyPaid(id);
        }

        if (order.Total != amount)
        {
            return new OrderErrors.AmountMismatch(order.Total, amount);
        }

        _orders[id] = order with { Status = OrderStatus.Paid };
        return Unit.Default;
    }
}
