# Sample.ErrorOr.Api

The same API written in [ErrorOr](https://github.com/amantinband/error-or) — the catalog lives in
ErrorOr''s own factory calls, and the wire code is read **out of the body**, so it is written exactly
once. Port **:5081**.

## The shape

```csharp
[ErrorCatalog("Orders")]
public static class OrderErrors
{
    [Error(404)]   // status only - the code "Orders.NotFound" is read from the line below
    public static Error NotFound => Error.NotFound("Orders.NotFound", "No such order.");
}

public ErrorOr<Order> GetById(Guid id) => /* returns OrderErrors.NotFound when missing */;

orders.MapGet("/{id:guid}", (Guid id, IOrderService s) => s.GetById(id).ToHttpResult());
```

Write the code twice and let it drift from the body literal, and `EAPI008` reports it.

## What was added, in order

1. `ErrorApi.ErrorOr` package reference (brings `ErrorApi.AspNetCore` + the generator along).
2. The catalog: `[Error(status)]` on the existing ErrorOr factory members — no new error types.
3. `builder.Services.AddErrorApi();`
4. Endpoints keep returning `ErrorOr<T>`; mapping is `.ToHttpResult()` from `ErrorApi.Interop`.
5. `app.MapOpenApi(); app.MapErrorContract();`

## What Swagger shows

The same contract as Sample.Api — `POST /orders/{id}/pay` → 200/404/409/422 with the `code` enum —
because the contract comes from the catalog, not from the result library. At runtime the first error
of a failed `ErrorOr<T>` resolves by its code through the generated `FindError` switch.

```bash
dotnet run --project samples/Sample.ErrorOr.Api
```

Then: `http://localhost:5081/swagger` · `/scalar` · `/openapi/errors.ts`.
