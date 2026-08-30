# Sample.Ardalis.Api

The same API in [Ardalis.Result](https://github.com/ardalis/Result), which has **no typed error and no
code slot at all** — a failure is a `ResultStatus` plus message strings. The identity therefore lives
in a catalog of factory members, with the code carried where Ardalis has room for it. Port **:5091**.

## The shape

```csharp
[ErrorCatalog("Orders")]
public static class OrderErrors
{
    [Error("Orders.NotFound", 404, Title = "Order not found")]
    public static Result NotFound() => Result.NotFound("Orders.NotFound");   // the code IS the message
}

public Result<Order> GetById(Guid id) => /* returns OrderErrors.NotFound() when missing */;

orders.MapGet("/{id:guid}", (Guid id, IOrderService s) => s.GetById(id).ToHttpResult());
```

## What was added, in order

1. `ErrorApi.ArdalisResult` package reference.
2. The factory catalog above — the one style Ardalis'' shape allows.
3. `builder.Services.AddErrorApi();`
4. Endpoints keep returning `Ardalis.Result.Result<T>`; mapping is `.ToHttpResult()` (statuses mapped the way `Ardalis.Result.AspNetCore` maps them: `Invalid` 400, `Error` 422, `Unavailable` 503).
5. `app.MapOpenApi(); app.MapErrorContract();`

## What Swagger shows

The shared contract. Resolution at runtime: a `ValidationError.ErrorCode` the catalog knows, then a
message that is a known code, then the bare `ResultStatus` — that last rung is deliberately weak,
because a failure without a catalog identity cannot be promised in a document.

```bash
dotnet run --project samples/Sample.Ardalis.Api
```

Then: `http://localhost:5091/swagger` · `/scalar` · `/openapi/errors.ts`.
