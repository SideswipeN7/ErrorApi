<img src="../../docs/images/logo.svg" alt="ErrorApi" width="300">

# ErrorApi.CSharpFunctionalExtensions

Maps [CSharpFunctionalExtensions](https://github.com/vkhorikov/CSharpFunctionalExtensions) onto Minimal
API results and ErrorApi's documented error catalog: every endpoint's OpenAPI document lists the
failures it can actually return, and the wire response carries a stable machine-readable `code` —
resolved at compile time by the ErrorApi source generator, with no reflection at runtime.

```bash
dotnet add package ErrorApi.CSharpFunctionalExtensions
```

The package brings the generator with it (`PrivateAssets="none"`), so referencing the adapter is the
whole setup.

## Before

A `Result<T, E>` maps to a response at runtime, in a mapper you wrote yourself — and the OpenAPI
document never learns about it:

```csharp
public Result<Order, OrderError> GetById(Guid id) =>
    _orders.TryGetValue(id, out var order) ? order : new OrderNotFound(id);

app.MapGet("/orders/{id:guid}", (Guid id, IOrderService s) =>
    s.GetById(id).Match(Results.Ok, e => Results.NotFound()));   // hand-rolled, per endpoint
// Document: 200 OK. The 404 exists — the frontend meets it in production.
```

## After

`Result<T, E>` is the sweet spot for ErrorApi, because the failure already **is** a type of your own —
that is exactly where the catalog entry goes:

```csharp
public abstract record OrderError;

[ErrorApi.Error("Orders.NotFound", 404, Title = "Order not found")]
public sealed record OrderNotFound(Guid Id) : OrderError;

public Result<Order, OrderError> GetById(Guid id) =>
    _orders.TryGetValue(out var order) ? order : new OrderNotFound(id);

builder.Services.AddErrorApi();
app.MapGet("/orders/{id:guid}", (Guid id, IOrderService s) => s.GetById(id).ToHttpResult());
// Document: 200 + 404 with code "Orders.NotFound", title, and an example body.
// Wire:     { "title": "Order not found", "status": 404, "code": "Orders.NotFound" }
```

The generator documents the endpoint wherever it sees a case constructed; at runtime the instance
resolves through a generated pattern switch — no reflection, native-AOT clean.

## The whole surface

| shape | success | failure |
| --- | --- | --- |
| `Result<T, E>.ToHttpResult()` | `200` with the value | `ProblemDetails` from `E` |
| `Result<T, E>.ToCreated(...)` / `ToCreatedAtUri(...)` | `201` | `ProblemDetails` |
| `Result<T, E>.ToNoContentResult()` | `204` | `ProblemDetails` |
| `UnitResult<E>.ToHttpResult()` | `204` | `ProblemDetails` |
| `Result.ToHttpResult()` / `Result<T>.ToHttpResult()` | `204` / `200` | see below |

A string-error `Result<T>` resolves only when the string is a known catalog code; anything else answers
as a 500 carrying the message — deliberately unhelpful as a contract, because a message is not one.
Prefer `Result<T, E>`.

## Versions

Pinned to the CSharpFunctionalExtensions version in this repository's `Directory.Packages.props`; the
test suite also runs against older releases in CI (`-p:CfeTestVersion=x.y.z`).
