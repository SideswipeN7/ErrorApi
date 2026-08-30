# Sample.Mediator.Api

The same API with every endpoint behind **MediatR** — the sample that proves the dispatch bridge:
`sender.Send(new GetOrder(id))` has no body to follow (`ISender` is implemented inside MediatR), so
the walk follows the **message type** to its handler instead. Port **:5085**.

## The shape

```csharp
public sealed record GetOrder(Guid Id) : IRequest<Result<Order>>;

public sealed class GetOrderHandler(OrderStore store) : IRequestHandler<GetOrder, Result<Order>>
{
    public Task<Result<Order>> Handle(GetOrder request, CancellationToken ct) =>
        Task.FromResult(store.Find(request.Id));   // returns OrderErrors.NotFound
}

var orders = app.MapGroup("/orders").AddErrorApiResults();   // results may be returned directly

orders.MapGet("/{id:guid}", async (Guid id, ISender sender) =>
    await sender.Send(new GetOrder(id)));   // Result<Order> as-is - the filter maps it; documents 404
```

## What was added, in order

1. `ErrorApi.AspNetCore` reference next to the existing MediatR setup.
2. The catalog (`[ErrorCatalog]` + `[Error]`), raised inside the handlers.
3. `builder.Services.AddErrorApi();`
4. Endpoints dispatch through `ISender`; under `AddErrorApiResults()` the awaited `Result<T>` is returned directly (the filter maps it), elsewhere `.ToHttpResult()` does.
5. `app.MapOpenApi(); app.MapErrorContract();`

## What Swagger shows

The same contract as the direct-call samples: the failures raised two layers behind the mediator land
on the endpoints that dispatch the messages. When a handler genuinely cannot be found, the walk does
not guess — `EAPI009` names the endpoint at build time instead.

```bash
dotnet run --project samples/Sample.Mediator.Api
```

Then: `http://localhost:5085/swagger` · `/scalar` · `/openapi/errors.ts`.
