# Sample.Shared.Errors

Not an API — a **class library** holding a shared catalog and the services that raise it. The library
side of the assembly-boundary story: a project that runs the generator and maps no endpoints bakes its
knowledge into its own assembly for consumers to read back.

## The shape

```csharp
[ErrorCatalog("Customers")]
public static class CustomerErrors
{
    [Error(404, Description = "No customer exists for the supplied identifier.")]
    public sealed record NotFound(Guid Id);

    [Error(410)]   // body-inferred wire code: "Very.Old.Retired", read from the implementation
    public static Error Retired { get; } = new("Very.Old.Retired", 410, "Retired");
}

public sealed class CustomerService : ICustomerService { /* raises the entries above */ }
```

## What was added, in order

1. `ErrorApi.Abstractions` + the generator (any ErrorApi package brings it).
2. The catalog and the services — ordinary library code.
3. Nothing else. A compilation with no endpoints is a library, so exporting is the default; the project
   file spells it out anyway: `<ErrorApiExportReachability>true</ErrorApiExportReachability>`.

## What gets baked into the assembly

```csharp
[assembly: CatalogExport("P:...Retired", "Very.Old.Retired")]                  // body-inferred code
[assembly: ReachabilityExport("M:...ICustomerService.Find(System.Guid)", ...)] // per public member
[assembly: ReachabilityExport("T:...PromoteCustomer", ...)]                    // per handled message
```

`Sample.Toolbox.Api` consumes all of it across the boundary — see its README for the consumer side.
