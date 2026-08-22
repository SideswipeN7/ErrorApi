<img src="../../docs/images/logo.svg" alt="ErrorApi" width="300">

# ErrorApi.LanguageExt

**Keep returning `Fin<T>` — and get an OpenAPI document that finally says which errors each endpoint can return.**

```bash
dotnet add package ErrorApi.LanguageExt
```

A language-ext `Error` carries a numeric code and a message. Neither says anything about HTTP, so the
mapping ends up hand-written per endpoint — and the document still stops at `200 OK`.

ErrorApi reads your error types at compile time, follows each endpoint handler through the call graph —
including through `IOrderService` into its implementation — and writes the answer into OpenAPI.

This package is the language-ext half: it turns a `Fin`, an `Either` or a bare `Error` into a problem
response that agrees with the document. It brings the generator with it; nothing else to install.

## Before / after

Same feature, same `Fin<T>`, same services. Here is every line that changes.

### 1. The error types

Before:

```csharp
using LanguageExt.Common;

public sealed record OrderNotFound(Guid Id) : Expected("Order not found", 404);
public sealed record OrderAlreadyPaid(Guid Id) : Expected("Order already paid", 409);
```

After — one attribute per type. The numeric code language-ext carries is not a wire contract, so
the attribute adds the code a client can switch on and the status the document will promise:

```csharp
using LanguageExt.Common;

[ErrorApi.Error("Orders.NotFound", 404, Title = "Order not found")]
public sealed record OrderNotFound(Guid Id) : Expected("Order not found", 404);

[ErrorApi.Error("Orders.AlreadyPaid", 409, Title = "Order already paid")]
public sealed record OrderAlreadyPaid(Guid Id) : Expected("Order already paid", 409);
```

> **Naming note.** language-ext and ErrorApi both ship a type called `Error`, so spell the attribute out
> as `[ErrorApi.Error(...)]` — or add `using ErrorAttribute = ErrorApi.ErrorAttribute;` to the file and
> keep writing `[Error(...)]`.

### 2. The service

**Not one line changes.** This is the part people expect to have to rewrite, and do not:

```csharp
public Fin<Order> GetById(Guid id) =>
    _orders.TryGetValue(id, out var order) ? order : new OrderNotFound(id);
```

### 3. The endpoint

Before — the status mapping is written by hand at every call site, and the `.Produces` calls are a
second copy of the same knowledge that goes stale the first time the service gains a failure:

```csharp
app.MapGet("/orders/{id:guid}", (Guid id, IOrderService s) =>
    s.GetById(id).Match<IResult>(
        order => TypedResults.Ok(order),
        error => error.Code switch
        {
            404 => TypedResults.Problem(statusCode: 404, title: "Order not found"),
            409 => TypedResults.Problem(statusCode: 409, title: "Order already paid"),
            _   => TypedResults.Problem(statusCode: 500),
        }))
    .Produces<ProblemDetails>(404)
    .Produces<ProblemDetails>(409);
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
{ "title": "Order not found", "status": 404, "detail": "Order not found", "code": "Orders.NotFound" }
```

And the generated TypeScript turns a `catch` full of guesses into a union the compiler checks — add a
failure server-side and the frontend build breaks instead of production:

```ts
// before: the shape is a guess, and the codes are folklore
catch (e) { if (e.status === 404) show("not found"); }
```

```ts
// after: generated from the same types the server compiled
export type GetOrdersByIdError = ApiProblem<"Orders.NotFound">;
```
## How an error is resolved

| Situation | Result |
| --- | --- |
| The error's type is annotated | The catalog's code, status and title. The error's `Message` becomes `detail`. |
| Not annotated, `Code` looks like a status | That status is used, with `Message` as `detail` and the type name as the code. |
| Anything else | `500`, with `Message` as `detail`. |

Resolution goes by **type** first, through a pattern switch the generator emits into your own assembly:

```csharp
public ErrorDescriptor? FindErrorForInstance(object? instance) => instance switch
{
    global::Shop.OrderNotFound => _errors[1],
    _ => null,
};
```

That is the entire lookup — no reflection, nothing for the trimmer to keep alive. Falling back to the
numeric code is a convenience for errors you did not write, not the intended path: an unannotated error
gets a type name for a code, which is not a contract a client should depend on.

## What you get

```csharp
finResult.ToHttpResult();                          // Fin<T> and Task<Fin<T>>
finResult.ToHttpResult(order => Results.Ok(order));
finResult.ToNoContentResult();                     // 204
finResult.ToCreated(order => $"/orders/{order.Id}");   // 201, location built from the created value
finResult.ToCreatedAtRoute("GetOrder", order => new() { ["id"] = order.Id });
eitherResult.ToHttpResult();                       // Either<Error, T>
error.ToProblem();                                 // an Error on its own
error.ToErrorApiError();                           // resolve without producing a response
```

Every method that resolves an error takes an optional `IErrorApiMetadata`, so the behaviour is testable
without standing up a host.

You also get, from the core package, a TypeScript contract with one union per endpoint:

```ts
export type GetOrdersByIdError = ApiProblem<"Orders.NotFound">;
```

A `switch` over `problem.code` stops compiling the moment the API gains a failure the client does not
handle.

## Compatibility

Built against LanguageExt.Core 4.4.9, and verified in CI against **4.4.0 and 4.4.9**. Targets `net10.0`
and is native-AOT clean.

The v5 line reshapes `Error` and the monad hierarchy; it is not supported yet. If you are on v5, open an
issue — the adapter is about eighty lines, and the compile-time half needs no changes at all.

## Full documentation

[github.com/SideswipeN7/EApi](https://github.com/SideswipeN7/EApi) — how discovery works, the `EAPI001`–`EAPI007`
diagnostics, the TypeScript contract, and the ErrorOr and OneOf adapters.
