# Sample.Wolverine.Api

The same API behind **Wolverine**, whose handlers implement no interface at all — they are matched by
convention (`OrderHandler.Handle(PlaceOrder)`), and so is the walk''s bridge: a `*Handler`/`*Consumer`
type with a `Handle`/`Consume` method taking the message. Port **:5088**.

## The shape

```csharp
public static class GetOrderHandler
{
    // No interface, no attribute - Wolverine finds this by convention, and so does the generator.
    public static Result<Order> Handle(GetOrder query, OrderStore store) => store.Find(query.Id);
}

orders.MapGet("/{id:guid}", (Guid id, IMessageBus bus) =>
    bus.InvokeAsync<Result<Order>>(new GetOrder(id)).ToHttpResult());
```

## What was added, in order

1. `ErrorApi.AspNetCore` reference next to the existing Wolverine setup.
2. The catalog, raised inside the convention handlers.
3. `builder.Services.AddErrorApi();`
4. Endpoints dispatch through `IMessageBus`; the awaited result maps with `.ToHttpResult()`.
5. `app.MapOpenApi(); app.MapErrorContract();`

## What Swagger shows

The same contract as the MediatR sample — the bridge is the message type either way; only the handler
shape differs, and the convention shape is covered without naming Wolverine anywhere in the generator.

```bash
dotnet run --project samples/Sample.Wolverine.Api
```

Then: `http://localhost:5088/swagger` · `/scalar` · `/openapi/errors.ts`.
