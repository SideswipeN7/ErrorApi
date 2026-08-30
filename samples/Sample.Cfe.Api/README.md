# Sample.Cfe.Api

The same API in [CSharpFunctionalExtensions](https://github.com/vkhorikov/CSharpFunctionalExtensions)
`Result<T, E>` — the sweet spot for this library, because the failure `E` already **is** a type of
your own: annotate it (or its concrete cases in a closed hierarchy) and the generated pattern switch
resolves instances at runtime. Port **:5092**.

## The shape

```csharp
[Error("Orders.NotFound", 404, Title = "Order not found")]
public sealed record OrderNotFound(Guid Id);

public Result<Order, OrderNotFound> GetById(Guid id) =>
    _orders.TryGetValue(id, out var order) ? order : new OrderNotFound(id);

orders.MapGet("/{id:guid}", (Guid id, IOrderService s) => s.GetById(id).ToHttpResult());
```

## What was added, in order

1. `ErrorApi.CSharpFunctionalExtensions` package reference.
2. `[Error]` on the typed failures (`E` of `Result<T, E>`).
3. `builder.Services.AddErrorApi();`
4. Endpoints return `Result<T, E>` / `UnitResult<E>`; mapping is `.ToHttpResult()` / `.ToCreatedAtUri(...)`.
5. `app.MapOpenApi(); app.MapErrorContract();`

## What Swagger shows

The shared contract. A string-error `Result<T>` still answers: a message that happens to be a known
catalog code resolves fully; anything else is a 500 carrying the message — deliberately unhelpful as a
contract, because a message is not one.

```bash
dotnet run --project samples/Sample.Cfe.Api
```

Then: `http://localhost:5092/swagger` · `/scalar` · `/openapi/errors.ts`.
