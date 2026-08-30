# Sample.Exceptions.Api

The same API with **no result type at all** — failures are thrown as annotated exceptions and answered
by the ErrorApi exception handler with the exact same `application/problem+json` body the result-based
samples produce. A client cannot tell which style the server was written in. Port **:5084**.

## The shape

```csharp
[Error(404, Title = "Order not found")]
public sealed class OrderNotFoundException(Guid id) : Exception($"No order {id}.");

public Order GetById(Guid id) => _orders.GetValueOrDefault(id) ?? throw new OrderNotFoundException(id);

orders.MapGet("/{id:guid}", (Guid id, IOrderService s) => Results.Ok(s.GetById(id)));
```

## What was added, in order

1. `ErrorApi.AspNetCore` reference — exceptions need no adapter package, because `System.Exception` needs no package.
2. `[Error(status)]` on the exception types; codes inferred from the names (`OrderNotFoundException` → `Orders.NotFound` under its catalog).
3. `builder.Services.AddErrorApi(); builder.Services.AddErrorApiExceptionHandler(); builder.Services.AddProblemDetails();`
4. `app.UseExceptionHandler();` — the handler resolves the thrown instance through the same generated type switch.
5. `app.MapOpenApi(); app.MapErrorContract();`

## What Swagger shows

The identical 200/404/409/422 contract: the walk treats an annotated `throw` like any other failure it
can reach, and the handler writes the response through `Error.ToProblem()` — the same call the result
path makes.

```bash
dotnet run --project samples/Sample.Exceptions.Api
```

Then: `http://localhost:5084/swagger` · `/scalar` · `/openapi/errors.ts`.
