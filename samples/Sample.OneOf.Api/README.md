# Sample.OneOf.Api

The same API as a [OneOf](https://github.com/mcintyre321/OneOf) discriminated union — and the sample
that shows a catalog **without `[ErrorCatalog]`**: each failure is its own record carrying an explicit
code, no shared prefix class anywhere. Port **:5082**.

## The shape

```csharp
// No [ErrorCatalog] - the type IS the identity, the code is spelled on it.
[Error("Orders.NotFound", 404, Title = "Order not found")]
public sealed record OrderNotFound(Guid Id);

[Error("Orders.AlreadyPaid", 409, Title = "Order already paid")]
public sealed record OrderAlreadyPaid(Guid Id);

public OneOf<Order, OrderNotFound, OrderAlreadyPaid> Pay(Guid id) => /* ... */;

orders.MapPost("/{id:guid}/pay", (Guid id, IOrderService s) => s.Pay(id).ToHttpResult());
```

At runtime the active failure arm resolves **by its type** through the generated pattern switch —
no reflection, which is what keeps this working under native AOT.

## What was added, in order

1. `ErrorApi.OneOf` package reference.
2. `[Error("code", status)]` on the union''s failure records (works for any hand-rolled union too).
3. `builder.Services.AddErrorApi();`
4. Endpoints return `OneOf<...>` (up to 7 failure arms); mapping is `.ToHttpResult()`.
5. `app.MapOpenApi(); app.MapErrorContract();`

## What Swagger shows

`POST /orders/{id}/pay` documents 200/404/409/422: every failure arm of the union that the walk can
reach becomes a documented response, keyed by the code on its record.

```bash
dotnet run --project samples/Sample.OneOf.Api
```

Then: `http://localhost:5082/swagger` · `/scalar` · `/openapi/errors.ts`.
