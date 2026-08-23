<img src="../../docs/images/logo.svg" alt="ErrorApi" width="300">

# ErrorApi.FluentResults

**Keep returning `Result<T>` — and get an OpenAPI document that finally says which errors each endpoint can return.**

```bash
dotnet add package ErrorApi.FluentResults
```

`Result.Fail("order not found")` carries a message and nothing else: no code a client can switch on, no
status, nothing a document could promise. That is why the mapping to `ProblemDetails` ends up
hand-written per endpoint, and why the document still stops at `200 OK`.

ErrorApi reads your error types at compile time, follows each endpoint handler through the call graph —
including through `IOrderService` into its implementation — and writes the answer into OpenAPI.

This package is the FluentResults half: it turns a `Result`, a `Result<T>` or a bare `IError` into a
problem response that agrees with the document. It brings the generator with it; nothing else to install.

## Before / after

Same feature, same `Result<T>`, same services. Here is every line that changes.

### 1. The error catalog

Before — the failure is a string, so nothing about it can be checked or documented:

```csharp
public Result<Order> GetById(Guid id) =>
    _orders.TryGetValue(id, out var order)
        ? Result.Ok(order)
        : Result.Fail($"No order {id}.");
```

After — model each failure as its own `Error` subclass, which is what FluentResults recommends anyway.
The type becomes the identity; the attribute adds the code and status it has no way to carry:

```csharp
using FluentError = FluentResults.Error;

[ErrorApi.ErrorCatalog("Orders")]
public static class OrderErrors
{
    [ErrorApi.Error(404)]  // -> "Orders.NotFound", title "Not found"
    public sealed class NotFound(Guid id) : FluentError($"No order {id}.");

    [ErrorApi.Error(409)]  // -> "Orders.AlreadyPaid", title "Already paid"
    public sealed class AlreadyPaid(Guid id) : FluentError($"Order {id} was already paid.");
}
```

> **Naming note.** FluentResults and ErrorApi both export a type called `Result`, so a file cannot import
> both namespaces — every mention of `Result` would be ambiguous. Let FluentResults keep the plain name
> and spell ours out as `[ErrorApi.Error(...)]`, as above.

### 2. The service

Only the failure value changes, from a string to the type that now means something:

```csharp
public Result<Order> GetById(Guid id) =>
    _orders.TryGetValue(id, out var order)
        ? Result.Ok(order)
        : Result.Fail(new OrderErrors.NotFound(id));
```

### 3. The endpoint

Before — the status mapping is written by hand at every call site, and the `.Produces` calls are a
second copy of the same knowledge that goes stale the first time the service gains a failure:

```csharp
app.MapGet("/orders/{id:guid}", (Guid id, IOrderService s) =>
    {
        var result = s.GetById(id);
        if (result.IsSuccess) return TypedResults.Ok(result.Value);

        return result.Errors[0].Message.Contains("No order")
            ? TypedResults.Problem(statusCode: 404, title: "Order not found")
            : TypedResults.Problem(statusCode: 500);
    })
    .Produces<ProblemDetails>(404);
```

After — the mapping comes from the catalog, and the documented responses are derived from the code:

```csharp
app.MapGet("/orders/{id:guid}", (Guid id, IOrderService s) => s.GetById(id).ToHttpResult());
```

### 4. Start-up

Before:

```csharp
builder.Services.AddOpenApi();

var app = builder.Build();
app.MapOpenApi();
```

After — one line, plus `using ErrorApi.Interop;` in the files that call `ToHttpResult()`:

```csharp
builder.Services.AddOpenApi();
builder.Services.AddErrorApi();   // generated overload — no reflection behind it

var app = builder.Build();
app.MapOpenApi();
app.MapErrorContract();           // optional: serves the TypeScript contract at /openapi/errors.ts
```

### 5. What the caller sees

Before, `GET /orders/{id}` promises only success in the document. The failures still happen; they are
simply not written down anywhere a client can read:

```jsonc
"responses": {
  "200": { "description": "OK", "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Order" } } } }
}
```

After:

```jsonc
"responses": {
  "200": { "description": "OK", "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Order" } } } },
  "404": {
    "description": "Not Found — Orders.NotFound",
    "content": {
      "application/problem+json": {
        "schema": {
          "required": ["status", "code"],
          "properties": {
            "status": { "enum": [404], "type": "integer" },
            "code":   { "enum": ["Orders.NotFound"], "type": "string" }
          }
        },
        "examples": { "Orders.NotFound": { "value": { "status": 404, "code": "Orders.NotFound" } } }
      }
    }
  }
}
```

The response body is RFC 9457 with the machine-readable code alongside:

```json
{ "title": "Not found", "status": 404, "detail": "No order 1111….", "code": "Orders.NotFound" }
```

And the generated TypeScript turns a `catch` full of guesses into a union the compiler checks — add a
failure server-side and the frontend build breaks instead of production:

```ts
// before: the shape is a guess, and the codes are folklore
catch (e) { if (e.message.includes("No order")) show("not found"); }
```

```ts
// after: generated from the same types the server compiled
export type GetOrdersByIdError = ApiProblem<"Orders.NotFound">;
```

## How an error is resolved

| Situation | Result |
| --- | --- |
| The error's type is annotated | The catalog's code, status and title. The error's `Message` becomes `detail`. |
| Not annotated, but carries `code` in `Metadata` | That entry, looked up by code. |
| Anything else | `500`, with `Message` as `detail`. |

Resolution goes by **type** first, through a pattern switch the generator emits into your own assembly:

```csharp
public ErrorDescriptor? FindErrorForInstance(object? instance) => instance switch
{
    global::Shop.OrderErrors.NotFound => _errors[1],
    _ => null,
};
```

That is the entire lookup — no reflection, nothing for the trimmer to keep alive. The metadata route
exists because `.WithMetadata("code", "Orders.NotFound")` is the idiomatic place to put a code in
FluentResults when you do not want a type per failure. A bare `Result.Fail("message")` answers as a 500
carrying the message, which is deliberately unhelpful as a contract — because a message is not one.

### More than one error

A FluentResults result can carry several. **The first one decides the status and the code**, because
that is what keeps the response matching the document that listed them. The rest are dropped by default:
the documented schema promises `code` and `status`, and adding a member that is in neither would break
the one guarantee this project exists to make.

Where accumulated validation failures matter more than that, opt in:

```csharp
FluentResultsHttpExtensions.IncludeAllErrors = true;
```

Each further error is then listed under an `errors` extension member. It is not part of the documented
schema, and the README says so rather than pretending otherwise.

## What you get

```csharp
result.ToHttpResult();                              // Result<T> and Task<Result<T>>
result.ToHttpResult(order => Results.Ok(order));
result.ToHttpResult();                              // Result -> 204 or ProblemDetails
result.ToNoContentResult();                         // 204
result.ToCreated(order => $"/orders/{order.Id}");   // 201, location built from the created value
result.ToCreatedAtRoute("GetOrder", order => new() { ["id"] = order.Id });
result.ToProblem();                                 // a failed result on its own
error.ToErrorApiError();                            // resolve without producing a response
```

Every method that resolves an error takes an optional `IErrorApiMetadata`, so the behaviour is testable
without standing up a host.

## A runnable version of all this

```bash
dotnet run --project samples/Sample.FluentResults.Api
```

The repository builds the same orders API seven times — on ErrorApi's own `Result<T>`, once per adapter,
once on plain exceptions and once behind MediatR — so you can diff the declaration styles against each
other. They produce byte-identical contracts. Browse the document at `http://localhost:5086/scalar`, or
read the generated model under `obj/generated/` after a build.

## Compatibility

Built against FluentResults 3.16.0, and verified in CI against **3.15.0 and 3.16.0**. Targets `net10.0`
and is native-AOT clean.

## Full documentation

[github.com/SideswipeN7/ErrorApi](https://github.com/SideswipeN7/ErrorApi) — how discovery works, the `EAPI001`–`EAPI009`
diagnostics, the TypeScript contract, and the ErrorOr, OneOf and language-ext adapters.
