# Sample.Controllers.Api

The same API on old-fashioned attribute-routed **controllers** — an action is a handler like any
other: the route comes from `[Route]`/`[HttpGet]` (tokens, constraints and rooted templates included),
and the walk starts at the action method. Port **:5089**.

## The shape

```csharp
[ApiController]
[Route("orders")]
public sealed class OrdersController(IOrderStore store) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public ActionResult<Order> GetById(Guid id) => store.Find(id).ToActionResult();   // documents 404

    [HttpPost("{id:guid}/pay")]
    public IResult Pay(Guid id, PayOrderRequest request) => store.Pay(id, request).ToHttpResult();
}
```

Actions inherited from a shared base controller are scanned too, along with a base `[Route]` and
attributes inherited through overrides — MVC''s rules, mirrored.

## What was added, in order

1. `ErrorApi.AspNetCore` reference next to `AddControllers()`.
2. The same catalog as the Minimal API samples.
3. `builder.Services.AddErrorApi();`
4. Actions return `Result<T>` mapped with `.ToActionResult()` (MVC''s vocabulary) or `.ToHttpResult()` (MVC executes `IResult` too).
5. `app.MapOpenApi(); app.MapErrorContract(); app.MapControllers();`

## What Swagger shows

`GET /orders/{id}` → 200/404 and `POST /orders/{id}/pay` → 200/404/409 — identical problem bodies to
the Minimal API samples, so mixing both surfaces in one application yields one consistent document.

```bash
dotnet run --project samples/Sample.Controllers.Api
```

Then: `http://localhost:5089/swagger` · `/scalar` · `/openapi/errors.ts`.
