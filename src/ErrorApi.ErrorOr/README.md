<img src="../../docs/images/logo.svg" alt="ErrorApi" width="300">

# ErrorApi.ErrorOr

**Keep returning `ErrorOr<T>` — and get an OpenAPI document that finally says which errors each endpoint can return.**

```bash
dotnet add package ErrorApi.ErrorOr
```

ErrorOr already gives you a failure that is a value instead of an exception. What it cannot give you is
the *contract*: the mapping to `ProblemDetails` happens at runtime, so the document still says `200 OK`
and your frontend finds out about `409 Orders.AlreadyPaid` in production.

ErrorApi reads your catalog at compile time, follows each endpoint handler through the call graph —
including through `IOrderService` into its implementation — and writes the answer into OpenAPI.

This package is the ErrorOr half: it teaches the runtime how to turn an `ErrorOr.Error` into a problem
response that agrees with the document. It brings the generator with it; nothing else to install.

## Before / after

Same feature, same `ErrorOr<T>`, same services. Here is every line that changes.

### 1. The error catalog

Before:

```csharp
using ErrorOr;

public static class OrderErrors
{
    public static Error NotFound => Error.NotFound("Orders.NotFound", "No such order.");
    public static Error AlreadyPaid => Error.Conflict("Orders.AlreadyPaid", "Already paid.");
}
```

After — one attribute per error, carrying the status and nothing else. The code is read out of the
`code:` argument that is already there, so it is never written twice and cannot drift from what the
document promises. The title comes from the member name read as a sentence:

```csharp
using ErrorOr;

public static class OrderErrors
{
    [ErrorApi.Error(404)]  // -> "Orders.NotFound", title "Not found"
    public static Error NotFound => Error.NotFound("Orders.NotFound", "No such order.");

    [ErrorApi.Error(409)]  // -> "Orders.AlreadyPaid", title "Already paid"
    public static Error AlreadyPaid => Error.Conflict("Orders.AlreadyPaid", "Already paid.");
}
```

Spell the code out as `[ErrorApi.Error("Orders.NotFound", 404)]` when the body has no `code:` argument
to read, or when the documented code should differ from it. If both are present and disagree, `EAPI008`
reports it rather than letting the document and the wire drift apart.

> **Naming note.** ErrorOr and ErrorApi both ship a type called `Error`, so spell the attribute out
> as `[ErrorApi.Error(...)]` — or add `using ErrorAttribute = ErrorApi.ErrorAttribute;` to the file and
> keep writing `[Error(...)]`.

### 2. The service

**Not one line changes.** This is the part people expect to have to rewrite, and do not:

```csharp
public ErrorOr<Order> GetById(Guid id) =>
    _orders.TryGetValue(id, out var order) ? order : OrderErrors.NotFound;
```

### 3. The endpoint

Before — the status mapping is written by hand at every call site, and the `.Produces` calls are a
second copy of the same knowledge that goes stale the first time the service gains a failure:

```csharp
app.MapGet("/orders/{id:guid}", (Guid id, IOrderService s) =>
    s.GetById(id).Match<IResult>(
        order => TypedResults.Ok(order),
        errors => errors[0].Type switch
        {
            ErrorType.NotFound => TypedResults.Problem(statusCode: 404, title: "Order not found"),
            ErrorType.Conflict => TypedResults.Problem(statusCode: 409, title: "Order already paid"),
            _ => TypedResults.Problem(statusCode: 500),
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
{ "title": "Order not found", "status": 404, "detail": "No such order.", "code": "Orders.NotFound" }
```

And the generated TypeScript turns a `catch` full of guesses into a union the compiler checks — add a
failure server-side and the frontend build breaks instead of production:

```ts
// before: the shape is a guess, and the codes are folklore
catch (e) { if (e.status === 404) show("not found"); }
```

```ts
// after: generated from the same catalog the server compiled
export type GetOrdersByIdError = ApiProblem<"Orders.NotFound">;
```
## How an error is resolved at runtime

| Situation | Result |
| --- | --- |
| The code is in the catalog | The catalog's status and title win — they are what the document promised. `Description` becomes `detail`. |
| Unknown code, built-in `ErrorType` | `Validation` → 400, `Unauthorized` → 401, `Forbidden` → 403, `NotFound` → 404, `Conflict` → 409, everything else → 500. |
| Unknown code, custom numeric type | Honoured directly when it is already an HTTP status, so `Error.Custom(409, …)` maps to 409. |

The catalog winning over `ErrorType` is deliberate: a client that read the document is entitled to the
status the document listed, even if the error was constructed with a different `ErrorType` somewhere.

## What you get

Mapping methods on `ErrorOr<T>` and `Task<ErrorOr<T>>`:

```csharp
result.ToHttpResult();                       // 200 with the value, or ProblemDetails
result.ToHttpResult(order => Results.Ok(order));
result.ToCreated(order => $"/orders/{order.Id}");   // 201, location built from the created value
result.ToCreatedAtRoute("GetOrder", order => new() { ["id"] = order.Id });
result.ToNoContentResult();                  // 204
error.ToProblem();                           // an ErrorOr.Error on its own
error.ToErrorApiError();                     // resolve without producing a response
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

Built against ErrorOr 2.1.1, and verified in CI against **1.10.0, 2.0.1 and 2.1.1**. Targets `net10.0`
and is native-AOT clean: resolution is a switch over string constants, never reflection.

## Full documentation

[github.com/SideswipeN7/EApi](https://github.com/SideswipeN7/EApi) — how discovery works, the `EAPI001`–`EAPI007`
diagnostics, the TypeScript contract, and the OneOf and language-ext adapters.
