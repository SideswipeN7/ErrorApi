using System.Collections.Concurrent;
using ErrorApi;
using FluentValidation;
using MediatR;

namespace Sample.MediatorValidation.Api.Orders;

/// <summary>What a command answers with when it succeeds.</summary>
public sealed record OrderPlaced(Guid Id, string Customer, decimal Total);

/// <summary>Places an order.</summary>
public sealed record PlaceOrder(string Customer, decimal Total) : IRequest<Result<OrderPlaced>>;

/// <summary>Cancels an order.</summary>
public sealed record CancelOrder(Guid Id, string Reason) : IRequest<Result>;

/// <summary>
/// The catalog. Note which of these the generator can reach and which it cannot — the endpoints in
/// <c>Program.cs</c> say where each one comes from.
/// </summary>
[ErrorCatalog("Orders")]
public static partial class OrderErrors
{
    [Error(StatusCodes.Status404NotFound, Description = "No order exists for the supplied identifier.")]
    public static partial Error NotFound { get; }

    [Error(StatusCodes.Status409Conflict, Description = "The customer already has an order in flight.")]
    public static partial Error DuplicateCustomer { get; }

    [Error(StatusCodes.Status410Gone, Description = "The order was cancelled earlier and will not come back.")]
    public static partial Error AlreadyCancelled { get; }
}

/// <summary>Failures that belong to the pipeline rather than to one feature.</summary>
[ErrorCatalog("Common")]
public static class CommonErrors
{
    /// <summary>
    /// Thrown by the validation behaviour and answered by <c>AddErrorApiExceptionHandler()</c>. It is an
    /// exception rather than a returned value because a behaviour is generic over the response and has no
    /// way to build one; the catalog treats it as an ordinary type-identified entry either way.
    /// </summary>
    [Error(StatusCodes.Status400BadRequest, Title = "Request failed validation")]
    public sealed class Validation(string message) : Exception(message);
}

/// <summary>Rules for <see cref="PlaceOrder"/>.</summary>
public sealed class PlaceOrderValidator : AbstractValidator<PlaceOrder>
{
    /// <summary>Builds the rules.</summary>
    public PlaceOrderValidator()
    {
        RuleFor(command => command.Customer).NotEmpty().WithMessage("Customer must not be empty.");
        RuleFor(command => command.Total).GreaterThan(0).WithMessage("Total must be greater than zero.");
    }
}

/// <summary>Rules for <see cref="CancelOrder"/>.</summary>
public sealed class CancelOrderValidator : AbstractValidator<CancelOrder>
{
    /// <summary>Builds the rules.</summary>
    public CancelOrderValidator()
    {
        RuleFor(command => command.Reason).NotEmpty().WithMessage("Reason must not be empty.");
    }
}

/// <summary>
/// The cross-cutting half of the pipeline: it validates every command and turns a failure into
/// <c>Common.Validation</c>.
/// </summary>
/// <remarks>
/// This type is <em>generic over the request</em>, which is the whole point of a behaviour — and also
/// the reason the generator cannot see it. Discovery bridges a dispatch through the message type, and
/// nothing in this application ever constructs <c>ValidationBehaviour&lt;PlaceOrder, …&gt;</c>: MediatR
/// closes the generic at runtime, from the container. So the 400 raised here reaches the client
/// correctly but never reaches the document, unless an endpoint declares it with
/// <c>[ProducesError]</c>. <c>Program.cs</c> does that for one of the two endpoints, so the contrast is
/// visible in the same OpenAPI document.
/// </remarks>
public sealed class ValidationBehaviour<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    /// <inheritdoc />
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var context = new ValidationContext<TRequest>(request);

        var failures = validators
            .Select(validator => validator.Validate(context))
            .SelectMany(result => result.Errors)
            .Where(failure => failure is not null)
            .ToList();

        if (failures.Count > 0)
        {
            // Thrown, not returned: the response carries the documented Common.Validation code even for
            // the endpoint whose document never mentions it.
            throw new CommonErrors.Validation(string.Join(" ", failures.Select(f => f.ErrorMessage)));
        }

        return await next().ConfigureAwait(false);
    }
}

/// <summary>The store the handlers reach through their own scope.</summary>
public interface IOrderRepository
{
    /// <summary>Stores a new order, or says why it cannot.</summary>
    Result<OrderPlaced> Place(string customer, decimal total);

    /// <summary>Marks an order cancelled, or says why it cannot.</summary>
    Result Cancel(Guid id);
}

/// <inheritdoc />
public sealed class InMemoryOrderRepository : IOrderRepository
{
    private static readonly ConcurrentDictionary<Guid, OrderPlaced> Orders = new();
    private static readonly ConcurrentDictionary<Guid, byte> Cancelled = new();

    /// <inheritdoc />
    public Result<OrderPlaced> Place(string customer, decimal total)
    {
        if (Orders.Values.Any(order => order.Customer == customer))
        {
            return OrderErrors.DuplicateCustomer;
        }

        var placed = new OrderPlaced(Guid.NewGuid(), customer, total);
        Orders[placed.Id] = placed;
        return placed;
    }

    /// <inheritdoc />
    public Result Cancel(Guid id)
    {
        if (!Orders.ContainsKey(id))
        {
            return OrderErrors.NotFound;
        }

        return Cancelled.TryAdd(id, 0) ? Result.Success() : OrderErrors.AlreadyCancelled;
    }
}

/// <summary>
/// Handles <see cref="PlaceOrder"/> in a scope of its own.
/// </summary>
/// <remarks>
/// The scope is the point: the repository is resolved from a child container rather than injected. That
/// is still an interface with an implementation in this compilation, so the walk follows it and the
/// failures it returns do reach the document. A separate scope is not a boundary; a generic pipeline is.
/// </remarks>
public sealed class PlaceOrderHandler(IServiceScopeFactory scopes) : IRequestHandler<PlaceOrder, Result<OrderPlaced>>
{
    /// <inheritdoc />
    public Task<Result<OrderPlaced>> Handle(PlaceOrder request, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IOrderRepository>();

        return Task.FromResult(repository.Place(request.Customer, request.Total));
    }
}

/// <inheritdoc cref="CancelOrder"/>
public sealed class CancelOrderHandler(IServiceScopeFactory scopes) : IRequestHandler<CancelOrder, Result>
{
    /// <inheritdoc />
    public Task<Result> Handle(CancelOrder request, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IOrderRepository>();

        return Task.FromResult(repository.Cancel(request.Id));
    }
}
