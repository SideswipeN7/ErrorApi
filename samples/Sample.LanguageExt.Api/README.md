# Sample.LanguageExt.Api

The same API in [language-ext](https://github.com/louthy/language-ext) `Fin<T>` — and the showcase of
**minimal onboarding**: the `Expected` subclasses already carry the message and the status, so a bare
`[Error]` is the whole annotation. Nothing is written twice. Port **:5083**.

## The shape

```csharp
[ErrorCatalog("Orders")]
public static class OrderErrors
{
    [Error, ErrorDescription("No order exists for the supplied identifier.")]
    public sealed record NotFound(Guid Id) : Expected("Order not found", 404);
    //      ^ code "Orders.NotFound" from the name; status 404 and title from the base constructor

    [Error]
    public sealed record AmountMismatch(decimal ExpectedTotal, decimal Actual)
        : Expected("Amount does not match the order total", 422);
}

public Fin<Order> GetById(Guid id) => /* returns new OrderErrors.NotFound(id) when missing */;

orders.MapGet("/{id:guid}", (Guid id, IOrderService s) => s.GetById(id).ToHttpResult());
```

## What was added, in order

1. `ErrorApi.LanguageExt` package reference (v4; `ErrorApi.LanguageExt.V5` is the same surface for the 5.x beta).
2. Bare `[Error]` on the existing `Expected` subclasses; `[ErrorDescription]` where a line of docs earns its place.
3. `builder.Services.AddErrorApi();`
4. Endpoints keep returning `Fin<T>` / `Either<Error, T>`; mapping is `.ToHttpResult()`.
5. `app.MapOpenApi(); app.MapErrorContract();`

## What Swagger shows

The same 200/404/409/422 contract as every other sample; the titles in the examples ("Order not
found") come from the `Expected` messages — read at compile time from the base constructor calls, and
returned identically at runtime by the instance-type switch.

```bash
dotnet run --project samples/Sample.LanguageExt.Api
```

Then: `http://localhost:5083/swagger` · `/scalar` · `/openapi/errors.ts`.
