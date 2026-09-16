# Getting started

The full integration story: the quickstart in detail, the exception style, and failures from packages nobody can annotate.

## Quickstart

**0. Install.** One package carries everything the app project needs:

```bash
dotnet add package ErrorApi
```

`ErrorApi` is a meta-package — dependencies only. It pulls `ErrorApi.AspNetCore` (the integration,
shipping the source generator and the `Error`/`Result` primitives from `ErrorApi.Abstractions`), and
on .NET 8/9 also `ErrorApi.Swashbuckle`, because there the document has to come through Swagger. Two
cases reach past it: a class library that only *declares* catalog entries references
`ErrorApi.Abstractions` alone, and a project already on ErrorOr, OneOf, language-ext, FluentResults,
Ardalis.Result or CSharpFunctionalExtensions adds its [adapter package](adapters.md) beside it.

**1. Declare the catalog.** Partial members; the generator writes the bodies.

```csharp
using ErrorApi;

[ErrorCatalog("Orders")]
public static partial class OrderErrors
{
    [Error(StatusCodes.Status404NotFound, Title = "Order not found")]
    public static partial Error NotFound { get; }

    [Error(StatusCodes.Status409Conflict, Detail = "Order {0} was already paid.")]
    public static partial Error AlreadyPaid(Guid orderId);
}
```

Partial *properties* are a C# 13 feature — on net8/net9 set `<LangVersion>latest</LangVersion>`, or
declare the entry as a partial method instead.

The attribute carries the status and nothing else it does not have to: `NotFound` becomes the code
`Orders.NotFound` and the title `Not found`. [Codes you do not have to type](catalog.md#codes-you-do-not-have-to-type)
has the rules.

**2. Return them.** Nothing special — `Error` converts implicitly into `Result<T>`.

```csharp
public Result<Order> GetById(Guid id) =>
    _orders.TryGetValue(id, out var order) ? order : OrderErrors.NotFound;
```

**3. Wire it up once.**

```csharp
builder.Services.AddOpenApi();
builder.Services.AddErrorApi();   // generated overload — no reflection behind it

app.MapGet("/orders/{id:guid}", (Guid id, IOrderService s) => s.GetById(id).ToHttpResult());
```

That is the whole integration. `AddErrorApi()` registers this assembly's compile-time model and hooks an `IOpenApiOperationTransformer` into every document.

**Prefer `TypedResults`?** Every mapping has a typed twin that answers with `Results<…, ProblemHttpResult>`, so ASP.NET documents the success schema straight from the endpoint signature — no transformer involved on the success half:

```csharp
// GET /orders/{id} → 200 (with the Order schema) + the generator's 404
app.MapGet("/orders/{id:guid}", Results<Ok<Order>, ProblemHttpResult> (Guid id, IOrderService s) =>
    s.GetById(id).ToTypedResult());
```

| `IResult` form | typed twin | success arm |
| --- | --- | --- |
| `ToHttpResult()` on `Result<T>` | `ToTypedResult()` | `Ok<T>` |
| `ToHttpResult()` on `Result` | `ToTypedResult()` | `NoContent` |
| `ToCreated(...)` | `ToTypedCreated(...)` | `Created<T>` |
| `ToCreatedAtRoute(...)` | `ToTypedCreatedAtRoute(...)` | `CreatedAtRoute<T>` |

The failure arm is always `ProblemHttpResult`, which carries no static status — that is exactly the hole the generated per-endpoint contract fills, so the error half of the document is identical in both styles.

**Or skip the mapping call entirely.** C# forbids user-defined conversions to an interface, so a
result can never implicitly become an `IResult` — but an endpoint filter gets the same ergonomics:

```csharp
var orders = app.MapGroup("/orders").AddErrorApiResults();

orders.MapGet("/{id:guid}", async (Guid id, ISender sender) =>
    await sender.Send(new GetOrder(id)));       // Result<Order>, returned as-is
```

`AddErrorApiResults()` maps a returned `Result`/`Result<T>` exactly as `ToHttpResult()` would
(success → 200/204, failure → the problem shape with the code) and rewrites the endpoint's 200
metadata so the document describes `T`, never the wrapper. The success value serializes from its
runtime type, which is the one part of ErrorApi native AOT cannot see through — under trimming or
AOT, prefer the explicit mapping calls.

**Flowing over a result.** `Match` folds to a value; its action-shaped relatives close or thread a
flow — `Switch` runs exactly one branch, `OnSuccess`/`OnFailure` run a side effect and hand the same
result back, so they slot into the middle of a chain (all with `Task`/`ValueTask` twins):

```csharp
service.Pay(id).Switch(order => _log.Paid(order), error => _alerts.Raise(error));

return (await service.PayAsync(id)
    .OnSuccess(order => _log.Paid(order))
    .OnFailure(error => _metrics.Bump(error.Code)))
    .ToHttpResult();
```

**Old-fashioned controllers work too.** An attribute-routed action is a handler like any other: the
generator finds the class by its `ControllerBase` ancestry (or `[ApiController]`), reads the route from
the attributes — `[controller]`/`[action]` tokens, constraints, rooted templates and all — and walks
the action method the same way it walks a lambda:

```csharp
[ApiController]
[Route("orders")]
public sealed class OrdersController(IOrderStore store) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public ActionResult<Order> GetById(Guid id) => store.Find(id).ToActionResult();   // documents 404

    [HttpPost("{id:guid}/pay")]
    public IResult Pay(Guid id, decimal amount) => store.Pay(id, amount).ToHttpResult();   // MVC executes IResult too
}
```

`ToActionResult()`/`ToCreatedActionResult(...)` speak MVC's own vocabulary and produce the identical
problem body, so mixing controllers and Minimal APIs in one application yields one consistent document.
Inheritance follows MVC's rules: actions on a shared base controller are scanned for every derived
controller, a `[Route]` on the base applies when the derived class declares none, and an override
without its own verb attribute inherits the overridden method's. Conventional (non-attribute) routing
has no compile-time template to read and stays out of scope.

---

---

## No result type? Plain exceptions work

A failure identified by a type is a shape the catalog already understands, and an exception class is
exactly that. Annotate it, throw it as you always have, and the endpoints that can reach it get
documented:

```csharp
[ErrorCatalog("Orders")]
public static class OrderErrors
{
    [Error(404, Description = "No order exists for the supplied identifier.")]
    public sealed class NotFoundException(Guid id) : Exception($"No order {id}.");
}

public Order GetById(Guid id) =>
    _orders.TryGetValue(id, out var order) ? order : throw new OrderErrors.NotFoundException(id);
```

The trailing `Exception` is dropped from the inferred code — a client does not care how the server
models the failure — so this is `Orders.NotFound`, the same code the `Result<T>` sample produces.

For the response, register the handler. It is deliberately not part of `AddErrorApi()`: taking over an
application's exception handling is not something a call by that name should do behind your back.

```csharp
builder.Services.AddErrorApi(x => x.HandleExceptions());   // or AddErrorApiExceptionHandler() beside AddErrorApi()

var app = builder.Build();
app.UseExceptionHandler();
```

```json
{ "title": "Not found", "status": 404, "detail": "No order 1111….", "code": "Orders.NotFound" }
```

The body comes from the same `Error.ToProblem()` the result path uses, so a client cannot tell which
style the server was written in. The annotated exception's `Message` becomes `detail` when the entry
carries none — a type you annotated is one whose message you wrote. Turn that off with
`h.UseExceptionMessageAsDetail(false)` when your messages quote things a client should not see; the
entry's `Detail` always wins and is the documented place for client-facing text. An exception the
catalog does not know is, so far, left untouched — the next two sections say what else can happen to it.

### Failures the catalog cannot see

`[Error]` needs a declaration and `ErrorMapping` needs a type. A failure that is only recognisable by
what the instance carries — a driver's error number, the status on a client exception — needs code,
and that code is a **global handler**. The `HandleExceptions` block says what happens to a thrown
exception, in the order it happens: the catalog first, then the handlers as added, then the fallback.

```csharp
public sealed class SqlExceptionHandler(ILogger<SqlExceptionHandler> log) : IGlobalExceptionHandler<SqlException>
{
    public Error Map(SqlException e) => e.Number switch
    {
        2627 or 2601 => DbErrors.Duplicate,      // catalog entries, so they are in the contract
        -2 => DbErrors.Timeout,
        _ => Error.None,                         // pass it on to the next handler, or the fallback
    };
}

builder.Services.AddErrorApi(x => x.HandleExceptions(h => h
    .Add<SqlException, SqlExceptionHandler>()                                   // typed: only SqlException reaches it
    .Add(e => e is OperationCanceledException ? ApiErrors.Cancelled : Error.None)   // the one-line form
    .MapUnhandledException()));
```

Three forms: `Add<TException, THandler>()` for a class that sees one exception type (derived types
included), `Add<THandler>()` for a class implementing the non-generic `IGlobalExceptionHandler` that
sees them all, and `Add(lambda)` for a rule with no dependencies. Handler classes are singletons with
constructor injection. The first handler to answer with something other than `Error.None` decides; an
annotated exception never reaches a handler at all.

The handlers are documented. A global handler is reachable from every endpoint by definition, so the
generator walks each handler's `Map` — and each lambda — exactly as it walks an endpoint handler, and
lists what it reads on every operation and in the TypeScript contract. That is why the rule is to
return catalog entries: a value built inside `Map` still answers, but nothing documents it.

### Nothing escapes as a bare 500

What the catalog and every handler passed on is normally left alone. When the API must never answer
with an undocumented 500, end the block with a fallback:

```csharp
builder.Services.AddErrorApi(x => x.HandleExceptions(h => h.MapUnhandledException()));
```

```json
{ "title": "Unexpected server error", "status": 500, "code": "Server.Unhandled", "traceId": "00-…" }
```

That is the built-in entry; the generator and the runtime compile the same constants, so the two
cannot disagree. An API with wording of its own passes a catalog entry like any other:

```csharp
[ErrorCatalog("Server")]
public static partial class ApiErrors
{
    [Error(500, Title = "Server failed", Description = "An unexpected failure; retry later, or quote the traceId to support.")]
    public static partial Error Failed { get; }
}

builder.Services.AddErrorApi(x => x.HandleExceptions(h => h.MapUnhandledException(ApiErrors.Failed)));
```

Two things are deliberate. The exception's message never becomes `detail` here, whatever
`UseExceptionMessageAsDetail` says — an unknown exception is exactly the one nobody composed for a
client. And the fallback is **documented**: it is reachable from every endpoint by definition, so the
generator lists it on every operation and in the TypeScript contract, and `EAPI010` stays quiet about
it. That only works when the argument is a catalog member the generator can see; a value built at
runtime still answers, and `EAPI014` says it is documented nowhere.

ASP.NET Core runs `IExceptionHandler`s in registration order. With a fallback set, ErrorApi answers
everything that reaches it, so register any handler of your own before `AddErrorApi(...)`.
`AddExceptionHandler(...)`, the previous name of the block, still compiles as an `[Obsolete]` alias
for one release.

---

---

## Failures from a package you do not own

`[Error]` has to sit on the declaration, which rules out anything shipped in a NuGet package. A
mapping says the same thing from the outside — the type is still the identity, the attribute supplies
the wire code and the status it has no way to carry:

```csharp
[assembly: ErrorMapping(typeof(StripeCardError), "Payments.CardDeclined", 402, Title = "Card declined")]
[assembly: ErrorMapping(typeof(GatewayTimeoutException), 504)]   // code inferred: "GatewayTimeout"
```

The entry lands in the catalog, the TypeScript contract and the generated type switch like any other,
so `TryGetCatalogError` and the exception handler resolve it at runtime with no special case.

Attaching it to an endpoint is a separate question, and worth being precise about. Where **your** code
constructs the type, the walk finds it with no help. Where the **library** raises it on its own there
is nothing in your source to follow, so name it on the endpoints that surface it:

```csharp
payments.MapPost("/", [ProducesError("Payments.GatewayTimeout")] (IPaymentService s) => s.Charge().ToHttpResult());
```

Or by type, which reads as what it is and survives a rename of the code:

```csharp
payments.MapPost("/", [ProducesError(typeof(GatewayTimeoutException))] (IPaymentService s) => s.Charge().ToHttpResult());
```

That is the trade the mapping buys: the code is defined once instead of per endpoint, and `EAPI005`
stops firing because it is now a real catalog entry.

---


---

[← back to the README](../README.md)
