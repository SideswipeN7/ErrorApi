<img src="../../docs/images/logo.svg" alt="ErrorApi" width="300">

# ErrorApi.ArdalisResult

Maps [Ardalis.Result](https://github.com/ardalis/Result) onto Minimal API results and ErrorApi's
documented error catalog: every endpoint's OpenAPI document lists the failures it can actually return,
and the wire response carries a stable machine-readable `code` — resolved at compile time by the
ErrorApi source generator, with no reflection at runtime.

```bash
dotnet add package ErrorApi.ArdalisResult
```

The package brings the generator with it (`PrivateAssets="none"`), so referencing the adapter is the
whole setup.

## Before

Ardalis.Result maps failures to responses at runtime — typically through `Ardalis.Result.AspNetCore`'s
`ToActionResult` — so the OpenAPI document never learns about them:

```csharp
public Result<Order> GetById(Guid id) =>
    _orders.TryGetValue(id, out var order) ? order : Result.NotFound();

app.MapGet("/orders/{id:guid}", (Guid id, IOrderService s) => s.GetById(id).ToMinimalApiResult());
// Document: 200 OK. The 404 exists — the frontend meets it in production.
// The body is whatever the mapper improvises; there is no stable code to switch on.
```

## After

Ardalis has no typed error and no code slot of its own, so the identity lives in a catalog of factory
members — the code carried where Ardalis has room for it, an error message or
`ValidationError.ErrorCode`, and the `[Error]` attribute tying it to a status and title:

```csharp
[ErrorCatalog("Orders")]
public static class OrderErrors
{
    [ErrorApi.Error("Orders.NotFound", 404, Title = "Order not found")]
    public static Result NotFound() => Result.NotFound("Orders.NotFound");

    [ErrorApi.Error("Orders.InvalidCustomer", 400, Title = "Customer must not be empty")]
    public static Result InvalidCustomer() => Result.Invalid(new ValidationError
    {
        ErrorCode = "Orders.InvalidCustomer",
        ErrorMessage = "Customer must not be empty.",
    });
}

public Result<Order> GetById(Guid id) =>
    _orders.TryGetValue(id, out var order) ? order : OrderErrors.NotFound();

builder.Services.AddErrorApi();
app.MapGet("/orders/{id:guid}", (Guid id, IOrderService s) => s.GetById(id).ToHttpResult());
// Document: 200 + 404 with code "Orders.NotFound", title, and an example body.
// Wire:     { "title": "Order not found", "status": 404, "code": "Orders.NotFound" }
```

The generator follows the handler into the service and its implementation, sees the factory in the call
graph, and documents the endpoint — nothing is declared at the call site.

## How a failure resolves at runtime

1. A `ValidationError.ErrorCode` the catalog knows — status, title and code come from the declaration
   the document was built from; the validation message becomes `detail`.
2. An error message that is a known code — same treatment.
3. Neither: the `ResultStatus` supplies the HTTP status (the same mapping `Ardalis.Result.AspNetCore`
   uses — `Invalid` 400, `Error` 422, `CriticalError` 500) and its name becomes the code. That is a
   deliberately weak contract: a failure without a catalog identity cannot be promised in a document.

Success statuses keep their Ardalis meaning: `Ok` → 200 with the value, `Created` → 201 with the
result's `Location`, `NoContent` → 204.

## Versions

Pinned to the Ardalis.Result version in this repository's `Directory.Packages.props`; the test suite
also runs against older releases in CI (`-p:ArdalisResultTestVersion=x.y.z`).
