# Sample.FluentResults.Api

The same API in [FluentResults](https://github.com/altmann/FluentResults) — the catalog lives in
annotated `Error` subclasses, and the sample also demos `IncludeAllErrors`: the first error decides
the status and code (so the response matches the document), and the rest ride along in a documented
optional `errors` array. Port **:5086**.

## The shape

```csharp
[ErrorCatalog("Orders")]
public static class OrderErrors
{
    [Error(404, Description = "No order exists for the supplied identifier.")]
    public sealed class NotFound(Guid id) : FluentResults.Error($"No order {id}.");
}

public Result<Order> GetById(Guid id) => /* Result.Fail(new OrderErrors.NotFound(id)) */;

orders.MapGet("/{id:guid}", (Guid id, IOrderService s) => s.GetById(id).ToHttpResult());
```

## What was added, in order

1. `ErrorApi.FluentResults` package reference.
2. `[Error(status)]` on the `FluentResults.Error` subclasses.
3. `builder.Services.AddErrorApi();` (+ optionally `FluentResultsHttpExtensions.IncludeAllErrors = true;`).
4. Endpoints keep returning `Result<T>`; mapping is `.ToHttpResult()`.
5. `app.MapOpenApi(); app.MapErrorContract();`

## What Swagger shows

The shared 200/404/409/422 contract, plus — with `IncludeAllErrors` — the `errors` member documented in
the problem schema, so a multi-failure validation result stays inside the promised shape.

```bash
dotnet run --project samples/Sample.FluentResults.Api
```

Then: `http://localhost:5086/swagger` · `/scalar` · `/openapi/errors.ts`.
