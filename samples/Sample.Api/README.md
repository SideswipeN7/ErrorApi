# Sample.Api

The reference API — Minimal APIs, route groups, interface dispatch, `[ProducesError]`, and the only
sample that builds with `PublishAot=true`, so the no-reflection claim is checked by the trim/AOT
analyzers on every build. Port **:5080**.

## The shape

```csharp
[ErrorCatalog("Orders")]                     // the classic catalog: prefix on the class...
public static partial class OrderErrors
{
    [Error(StatusCodes.Status404NotFound, Title = "Order not found")]
    public static partial Error NotFound { get; }        // ...codes built from the names

    [Error(StatusCodes.Status409Conflict, Detail = "Order {0} was already paid and cannot be paid again.")]
    public static partial Error AlreadyPaid(Guid orderId);
}

public Result<Order> GetById(Guid id) => /* service code returns OrderErrors.NotFound when missing */;

orders.MapGet("/{id:guid}", (Guid id, IOrderService s) => s.GetById(id).ToTypedResult());
```

Nothing on the endpoint declares which errors come back — the generator walks the handler through
`IOrderService` into the implementation and finds them.

## What was added, in order

1. Project references: `ErrorApi.AspNetCore` (+ the generator as an analyzer; via NuGet one package does both).
2. The catalog: `[ErrorCatalog]` classes with `[Error]` members (`OrderErrors`, `CommonErrors`).
3. `builder.Services.AddErrorApi();` — one line, registers the generated model and the OpenAPI transformer.
4. Endpoints returning `Result<T>` mapped with `.ToHttpResult()` / `.ToTypedResult()` / `.ToTypedCreatedAtRoute(...)`.
5. `app.MapOpenApi(); app.MapErrorContract();` — the document and the served TypeScript contract.

## What Swagger shows

- `POST /orders/{id}/pay` → **200**, **404** (`Orders.NotFound`), **409** (`Orders.AlreadyPaid`),
  **422** (`Orders.AmountMismatch`, `Orders.CurrencyMismatch` — two codes, one status, both listed in
  the `code` enum with their own examples).
- `GET /orders/{id}` → **200**, **404**; `GET /orders` → **200**, **429** via `[ProducesError("Common.RateLimited")]`.
- Every error response is `application/problem+json` with a `code` member — the same body the endpoint
  actually returns.

```bash
dotnet run --project samples/Sample.Api
```

Then: `http://localhost:5080/swagger` · `/scalar` · `/openapi/v1.json` · `/openapi/errors.ts`, or
`dotnet run --project samples/Sample.Api -- --emit-error-contract out.ts` for the build-step form.
