using System.Collections.Concurrent;
using ErrorApi;

namespace Sample.Shared.Errors;

/// <summary>A customer as returned to clients.</summary>
public sealed record Customer(Guid Id, string Name, bool Premium);

/// <summary>
/// The shared catalog. This project maps no endpoints, so the generator treats it as a library: it
/// walks the public surface below and bakes what each member can reach into the assembly as
/// <c>[assembly: ReachabilityExport]</c>, for the API project to read back through the reference.
/// </summary>
[ErrorCatalog("Customers")]
public static partial class CustomerErrors
{
    [Error(404, Description = "No customer exists for the supplied identifier.")]
    public static partial Error NotFound { get; }

    [Error(409, Description = "A customer with this name already exists.")]
    public static partial Error Duplicate { get; }

    [Error(409, Description = "The customer is already premium.")]
    public static partial Error AlreadyPremium { get; }
}

/// <summary>
/// A user-implemented entry whose wire code lives in the body — rule 2 of code inference. A consumer
/// cannot read this body through a metadata reference, which is exactly what
/// <c>[assembly: CatalogExport]</c> exists for: this assembly resolved <c>Very.Old.Retired</c> at its
/// own build and exported the resolution, so the consumer documents the code that is really on the wire.
/// </summary>
public static class LegacyErrors
{
    [Error(410, Description = "The customer record was retired during the 2019 migration.")]
    public static Error Retired { get; } = new("Very.Old.Retired", 410, "Customer retired");
}

/// <summary>The store, behind an interface the API project can only see as metadata.</summary>
public interface ICustomerService
{
    /// <summary>Reads one customer, or says why it cannot.</summary>
    Result<Customer> Find(Guid id);

    /// <summary>Registers a customer, or says why it cannot.</summary>
    Result<Customer> Register(string name);
}

/// <inheritdoc />
public sealed class CustomerService : ICustomerService
{
    private static readonly Guid RetiredMarker = Guid.Empty;
    private readonly ConcurrentDictionary<Guid, Customer> _customers = new();

    /// <inheritdoc />
    public Result<Customer> Find(Guid id)
    {
        if (id == RetiredMarker)
        {
            // The 410 whose code only this assembly's build could infer.
            return LegacyErrors.Retired;
        }

        return _customers.TryGetValue(id, out var customer) ? customer : CustomerErrors.NotFound;
    }

    /// <inheritdoc />
    public Result<Customer> Register(string name)
    {
        if (_customers.Values.Any(existing => existing.Name == name))
        {
            return CustomerErrors.Duplicate;
        }

        var customer = new Customer(Guid.NewGuid(), name, Premium: false);
        _customers[customer.Id] = customer;
        return customer;
    }

    internal Result<Customer> Promote(Guid id)
    {
        var lookup = Find(id);
        if (lookup.IsFailure)
        {
            return lookup;
        }

        if (lookup.Value.Premium)
        {
            return CustomerErrors.AlreadyPremium;
        }

        var promoted = lookup.Value with { Premium = true };
        _customers[promoted.Id] = promoted;
        return promoted;
    }
}

/// <summary>Promotes a customer to premium.</summary>
public sealed record PromoteCustomer(Guid Id);

/// <summary>
/// Handles <see cref="PromoteCustomer"/>. The consumer never sees this class — it dispatches the
/// message through <see cref="IDispatcher"/>, whose implementation also lives here. The generator in
/// this project unions what the handler can reach under the <em>message's</em> identity
/// (<c>[assembly: ReachabilityExport("T:…PromoteCustomer", …)]</c>), which is what the consumer's
/// dispatch bridge looks up.
/// </summary>
public sealed class PromoteCustomerHandler(ICustomerService customers)
{
    /// <inheritdoc cref="PromoteCustomer"/>
    public Result<Customer> Handle(PromoteCustomer message) =>
        ((CustomerService)customers).Promote(message.Id);
}

/// <summary>A deliberately tiny message dispatcher, standing in for any bus.</summary>
public interface IDispatcher
{
    /// <summary>Routes a message to its handler.</summary>
    Result<Customer> Send(PromoteCustomer message);
}

/// <inheritdoc />
public sealed class Dispatcher(PromoteCustomerHandler handler) : IDispatcher
{
    /// <inheritdoc />
    public Result<Customer> Send(PromoteCustomer message) => handler.Handle(message);
}
