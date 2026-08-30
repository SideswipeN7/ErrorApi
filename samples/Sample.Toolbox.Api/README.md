# Sample.Toolbox.Api

The toolbox: every advanced feature in one API, most importantly the **consumer side of the assembly
boundary** — everything this API documents about customers comes from `Sample.Shared.Errors`, whose
source this compilation never sees. Port **:5090**.

## The shape

```csharp
// Composition: this API''s own model first, plus the referenced library''s generated model.
builder.Services.AddErrorApi(x => x.Include(Sample.Shared.Errors.ErrorApiModel.Metadata));

// 404 + body-inferred 410 discovered across the boundary - the walk continues through the exports:
customers.MapGet("/{id:guid}", (Guid id, ICustomerService service) => service.Find(id).ToHttpResult());

// A dispatch whose handler lives in the other assembly - bridged by the message export:
customers.MapPost("/{id:guid}/promote", (Guid id, IDispatcher d) => d.Send(new PromoteCustomer(id)).ToHttpResult());

// A package exception nobody can annotate - mapped at the assembly level, declared by type:
[assembly: ErrorMapping(typeof(TimeoutException), "Gateway.Timeout", 504)]
app.MapGet("/gateway/ping", [ProducesError(typeof(TimeoutException))] () => ...);

// The implicit catalog: no [Error] anywhere - membership plus the class defaults do the declaring.
[ErrorCatalog("Toolbox.Flags", StatusCodes.Status403Forbidden)]
public static partial class FlagErrors
{
    public static partial Error FeatureDisabled { get; }                 // 403
    [ErrorStatusCode(StatusCodes.Status423Locked)]
    public static partial Error TemporarilyLocked { get; }               // 423
}
```

## What was added, in order

1. Project reference to `Sample.Shared.Errors` (+ `ErrorApi.AspNetCore` and the generator).
2. `<ErrorApiIncludeAssemblies>Sample.Shared.Errors</ErrorApiIncludeAssemblies>` in the csproj — the
   consumer names the layers it trusts the contract to come from.
3. `AddErrorApi(x => x.Include(...))` — so a failure whose **type** is declared in the library resolves
   by instance in this process.
4. `AddErrorApiExceptionHandler()` + `UseExceptionHandler()` for the thrown 504.
5. Endpoints; `app.MapOpenApi(); app.MapErrorContract();`

## What Swagger shows

- `GET /customers/{id}` → 200, **404**, **410** — the 410''s wire code is `Very.Old.Retired`, resolved
  from the library''s body and carried across the boundary by its export.
- `POST /customers/{id}/promote` → failures of a handler this compilation cannot see.
- `GET /gateway/ping` → **504** declared by exception type; `GET /flags/{name}` → **403**/**423** from
  the attribute-free catalog.

```bash
dotnet run --project samples/Sample.Toolbox.Api
```

Then: `http://localhost:5090/swagger` · `/scalar` · `/openapi/errors.ts`.
