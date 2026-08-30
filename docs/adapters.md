# Adapters

One adapter per result library, all feeding the same generator and producing the same contract.

## Bring your own Result type

`[Error]` has two modes, and the second is what makes the adapter packages possible.

| On | Mode | The generator |
| --- | --- | --- |
| `static partial Error` member | **generated** | writes the implementation, the `Codes` constants and the descriptor |
| a type, a field, or a member you implement yourself | **declarative** | writes nothing; records the entry and learns to recognise it in the call graph |

Declarative mode means the catalog can be expressed in someone else's error type. Everything downstream — discovery, OpenAPI, the TypeScript contract, the diagnostics — is unchanged.

### `ErrorApi.ErrorOr`

The code is the anchor: annotate the members that produce ErrorOr errors, and the runtime mapping looks the code back up in the catalog, so the status a client receives is the one the document promised.

```csharp
public static class OrderErrors
{
    [ErrorApi.Error("Orders.NotFound", 404, Title = "Order not found")]
    public static Error NotFound => Error.NotFound("Orders.NotFound", "No such order.");
}

orders.MapGet("/{id:guid}", (Guid id, IOrderService s) => s.GetById(id).ToHttpResult());
```

A code the catalog has never seen still maps sensibly: `ErrorType` supplies the status (`NotFound` → 404, `Conflict` → 409, `Validation` → 400 …), and a custom numeric type that already looks like an HTTP status is honoured.

### `ErrorApi.OneOf` — and any discriminated union

With a union the failure *is* a type, so that is where the entry goes. The generator documents the endpoint wherever it sees the case constructed.

```csharp
[Error("Orders.NotFound", 404, Title = "Order not found")]
public sealed record OrderNotFound(Guid Id);

public OneOf<Order, OrderNotFound, OrderAlreadyPaid> Pay(Guid id) => new OrderNotFound(id);

orders.MapPost("/{id:guid}/pay", (Guid id, IOrderService s) => s.Pay(id).ToHttpResult());
```

The first type argument is the success value; every later one is a failure. For a hand-rolled union with no library at all, `OneOfHttpExtensions.Problem(failure)` is the failure branch:

```csharp
return outcome switch
{
    Order order => TypedResults.Ok(order),
    var failure => OneOfHttpExtensions.Problem(failure),
};
```

### `ErrorApi.FluentResults`

`Result.Fail("order not found")` carries a message and nothing else — no code, no status, nothing a
document could promise. Model each failure as its own `Error` subclass, which is what FluentResults
recommends anyway, and the type becomes the identity:

```csharp
using FluentError = FluentResults.Error;

[ErrorApi.ErrorCatalog("Orders")]
public static class OrderErrors
{
    [ErrorApi.Error(404)]
    public sealed class NotFound(Guid id) : FluentError($"No order {id}.");
}

public Result<Order> GetById(Guid id) =>
    _orders.TryGetValue(id, out var order) ? Result.Ok(order) : Result.Fail(new OrderErrors.NotFound(id));

orders.MapGet("/{id:guid}", (Guid id, IOrderService s) => s.GetById(id).ToHttpResult());
```

A result carrying several errors answers with the first one — that is what keeps the response matching
the document. `.WithMetadata("code", …)` is the second way in, for failures you do not want a type for.
A bare `Result.Fail("message")` becomes a 500 carrying the message, which is deliberately unhelpful as
a contract, because a message is not one.

> **Naming note.** FluentResults and ErrorApi both export `Result`, so a file cannot import both
> namespaces. Let FluentResults keep the plain name and spell ours out as `[ErrorApi.Error(...)]`.

### `ErrorApi.LanguageExt`

Your `Expected` subclasses already carry a message and a status, so a bare `[Error]` is the whole
onboarding — the status and title are read from the base constructor call, the wire code from the name:

```csharp
[ErrorCatalog("Orders")]
public static class OrderErrors
{
    [Error]   // -> "Orders.NotFound", 404, "Order not found" — nothing written twice
    public sealed record NotFound(Guid Id) : Expected("Order not found", 404);
}

public Fin<Order> GetById(Guid id) => new OrderErrors.NotFound(id);

orders.MapGet("/{id:guid}", (Guid id, IOrderService s) => s.GetById(id).ToHttpResult());
```

Resolution goes by type first, through a generated pattern switch. Failing that the numeric code is used when it already looks like an HTTP status; everything else becomes a 500.

> **Naming note.** ErrorOr and language-ext both ship a type called `Error`. When both namespaces are imported, spell ours out as `[ErrorApi.Error(...)]` or add `using ErrorAttribute = ErrorApi.ErrorAttribute;`.

### `ErrorApi.ArdalisResult`

Ardalis.Result has no typed error and no code slot at all — a failure is a `ResultStatus` plus message
strings. The identity therefore lives in a catalog of factory members, with the code carried where
Ardalis has room for it: an error message, or `ValidationError.ErrorCode`.

```csharp
[ErrorCatalog("Orders")]
public static class OrderErrors
{
    [ErrorApi.Error("Orders.NotFound", 404, Title = "Order not found")]
    public static Result NotFound() => Result.NotFound("Orders.NotFound");
}

public Result<Order> GetById(Guid id) =>
    _orders.TryGetValue(id, out var order) ? order : OrderErrors.NotFound();

orders.MapGet("/{id:guid}", (Guid id, IOrderService s) => s.GetById(id).ToHttpResult());
```

Resolution: a `ValidationError.ErrorCode` the catalog knows, then an error message that is a known
code, then the `ResultStatus` alone — status mapped the way `Ardalis.Result.AspNetCore` maps it
(`Invalid` 400, `Error` 422), the status's name as the code. That last rung is deliberately weak: a
failure without a catalog identity cannot be promised in a document, and the adapter README says so.

### `ErrorApi.CSharpFunctionalExtensions`

`Result<T, E>` is the sweet spot: the failure already **is** a type of your own, so that is where the
catalog entry goes — annotate `E` (or its concrete cases) and the generated pattern switch resolves the
instance at runtime.

```csharp
public abstract record OrderError;

[ErrorApi.Error("Orders.NotFound", 404, Title = "Order not found")]
public sealed record OrderNotFound(Guid Id) : OrderError;

public Result<Order, OrderError> GetById(Guid id) =>
    _orders.TryGetValue(id, out var order) ? order : new OrderNotFound(id);

orders.MapGet("/{id:guid}", (Guid id, IOrderService s) => s.GetById(id).ToHttpResult());
```

`UnitResult<E>` maps to 204/problem. A string-error `Result<T>` resolves only when the string is a
known catalog code; anything else is a 500 carrying the message, because a message is not a contract.

### How an adapter reaches the catalog

Adapters never reflect. The generator emits a pattern switch over the annotated types and a switch over the codes:

```csharp
public ErrorDescriptor? FindErrorForInstance(object? instance) => instance switch
{
    global::Shop.OrderAlreadyPaid => _errors[0],
    global::Shop.OrderNotFound    => _errors[1],
    _ => null,
};
```

`AddErrorApi()` publishes the model on `ErrorApiRuntime.Metadata`, which is what lets `result.ToHttpResult()` work as a plain extension method with no service provider in scope. Every adapter also has an overload taking `IErrorApiMetadata` explicitly, and tests that stand up hosts one after another can hold the static in a scope: `using (ErrorApiRuntime.Use(metadata)) { … }` restores the previous model on dispose. The model stays one per process — parallel hosts should pass metadata explicitly.

---


---

[← back to the README](../README.md)
