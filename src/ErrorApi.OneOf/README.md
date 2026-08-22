<img src="https://raw.githubusercontent.com/SideswipeN7/EApi/main/docs/images/logo.svg" alt="ErrorApi" width="300">

# ErrorApi.OneOf

**Return a union from your services — and get an OpenAPI document that lists every case the endpoint can produce.**

```bash
dotnet add package ErrorApi.OneOf
```

With a union the failure *is* a type: `OneOf<Order, OrderNotFound, OrderAlreadyPaid>` already says
everything a caller needs. What it does not say is what any of that means over HTTP, so the document
still stops at `200 OK`.

ErrorApi reads the union cases at compile time, follows each endpoint handler through the call graph —
including through `IOrderService` into its implementation — and writes the answer into OpenAPI.

This package is the OneOf half. It also covers **hand-rolled discriminated unions**: if you model your
cases as a closed hierarchy without any library, the failure branch works exactly the same.

## Before / after

Same feature, same union, same services. Here is every line that changes.

### 1. The union cases

Before:

```csharp
public sealed record OrderNotFound(Guid Id);
public sealed record OrderAlreadyPaid(Guid Id);
```

After — one attribute per case. The records are otherwise untouched; the attribute says what the
case means over HTTP, which the union itself has no way to express:

```csharp
using ErrorApi;

[Error("Orders.NotFound", 404, Title = "Order not found")]
public sealed record OrderNotFound(Guid Id);

[Error("Orders.AlreadyPaid", 409, Title = "Order already paid")]
public sealed record OrderAlreadyPaid(Guid Id);
```

### 2. The service

**Not one line changes.** This is the part people expect to have to rewrite, and do not:

```csharp
public OneOf<Order, OrderNotFound, OrderAlreadyPaid> Pay(Guid id)
{
    if (!_orders.TryGetValue(id, out var order)) return new OrderNotFound(id);
    if (order.Status == OrderStatus.Paid)       return new OrderAlreadyPaid(id);

    _orders[id] = order with { Status = OrderStatus.Paid };
    return order;
}
```

### 3. The endpoint

Before — the status mapping is written by hand at every call site, and the `.Produces` calls are a
second copy of the same knowledge that goes stale the first time the service gains a failure:

```csharp
app.MapPost("/orders/{id:guid}/pay", (Guid id, IOrderService s) =>
    s.Pay(id).Match<IResult>(
        order => TypedResults.Ok(order),
        _     => TypedResults.Problem(statusCode: 404, title: "Order not found"),
        _     => TypedResults.Problem(statusCode: 409, title: "Order already paid")))
    .Produces<ProblemDetails>(404)
    .Produces<ProblemDetails>(409);
```

After — the mapping comes from the catalog, and the documented responses are derived from the code:

```csharp
app.MapPost("/orders/{id:guid}/pay", (Guid id, IOrderService s) => s.Pay(id).ToHttpResult());
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

Before, `POST /orders/{id}/pay` promises only success in the document. The failures still happen; they are
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
  },
  "409": {
    "description": "Conflict — Orders.AlreadyPaid",
    "content": {
      "application/problem+json": {
        "schema": {
          "required": ["status", "code"],
          "properties": {
            "status": { "enum": [409], "type": "integer" },
            "code":   { "enum": ["Orders.AlreadyPaid"], "type": "string" }
          }
        },
        "examples": { "Orders.AlreadyPaid": { "value": { "status": 409, "code": "Orders.AlreadyPaid" } } }
      }
    }
  }
}
```

The response body is RFC 9457 with the machine-readable code alongside:

```json
{ "title": "Order already paid", "status": 409, "code": "Orders.AlreadyPaid" }
```

And the generated TypeScript turns a `catch` full of guesses into a union the compiler checks — add a
failure server-side and the frontend build breaks instead of production:

```ts
// before: the shape is a guess, and the codes are folklore
catch (e) { if (e.status === 409) show("already paid"); }
```

```ts
// after: generated from the same cases the server compiled
export type PostOrdersByIdPayError =
  | ApiProblem<"Orders.AlreadyPaid">
  | ApiProblem<"Orders.NotFound">;
```
## Hand-rolled unions

No OneOf needed. Annotate the cases the same way and use `Problem` as the failure branch:

```csharp
public abstract record PayOutcome;
public sealed record Paid(Order Order) : PayOutcome;

[Error("Orders.NotFound", 404, Title = "Order not found")]
public sealed record OrderNotFound(Guid Id) : PayOutcome;

app.MapPost("/orders/{id:guid}/pay", (Guid id, IOrderService s) => s.Pay(id) switch
{
    Paid paid   => TypedResults.Ok(paid.Order),
    var failure => OneOfHttpExtensions.Problem(failure),
});
```

The generator documents the endpoint from the `new OrderNotFound(...)` it finds in the service, and
`Problem` resolves the instance back to the same entry. Annotate the concrete cases, not the abstract
base — an abstract type cannot identify a single failure, and the generator says so (`EAPI003`).

## How a case is resolved

The generator emits a pattern switch over your annotated types into your own assembly:

```csharp
public ErrorDescriptor? FindErrorForInstance(object? instance) => instance switch
{
    global::Shop.OrderAlreadyPaid => _errors[0],
    global::Shop.OrderNotFound    => _errors[1],
    _ => null,
};
```

That is the entire lookup. No reflection, no type dictionary built at startup, nothing for the trimmer
to keep alive. A case with no `[Error]` on it falls back to a `500` carrying the type name, which is a
signal you forgot to annotate it rather than a silent success.

## What you get

```csharp
result.ToHttpResult();                          // OneOf<T, TError>
result.ToHttpResult(order => Results.Ok(order));
result.ToHttpResult();                          // OneOf<T, TError1, TError2> and one more arity
result.ToNoContentResult();                     // 204
result.ToCreated(order => $"/orders/{order.Id}");   // 201, location built from the created value
result.ToCreatedAtRoute("GetOrder", order => new() { ["id"] = order.Id });
OneOfHttpExtensions.Problem(failure);           // any annotated instance
```

You also get, from the core package, a TypeScript contract with one union per endpoint:

```ts
export type PostOrdersByIdPayError =
  | ApiProblem<"Orders.AlreadyPaid">
  | ApiProblem<"Orders.NotFound">;
```

A `switch` over `problem.code` stops compiling the moment the API gains a failure the client does not
handle.

## Compatibility

Built against OneOf 3.0.271, and verified in CI against **3.0.263 and 3.0.271**. Targets `net10.0` and is
native-AOT clean.

## Full documentation

[github.com/SideswipeN7/EApi](https://github.com/SideswipeN7/EApi) — how discovery works, the `EAPI001`–`EAPI007`
diagnostics, the TypeScript contract, and the ErrorOr and language-ext adapters.
