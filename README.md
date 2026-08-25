<img src="https://raw.githubusercontent.com/SideswipeN7/ErrorApi/main/docs/images/logo.svg" alt="ErrorApi" width="340">

**A Roslyn source generator that turns a `Result<T>` error catalog into a Minimal API error mapper *and* an OpenAPI document that actually lists the failures — plus a TypeScript union for the client.**

Result libraries fixed the return type. They did not fix the contract: `ErrorOr`, `FluentResults` and friends map a failure to `ProblemDetails` at runtime, so the OpenAPI document still says `200 OK` and nothing else. The frontend has no idea a `409 Orders.AlreadyPaid` exists until it happens in production.

ErrorApi resolves that at compile time — and it does so whether you use its own `Result<T>`, ErrorOr, OneOf, language-ext, a hand-rolled discriminated union, or no result type at all: [plain exceptions work too](#no-result-type-plain-exceptions-work).

```csharp
[Error("Orders.AlreadyPaid", 409, Title = "Order already paid",
    Detail = "Order {0} was already paid and cannot be paid again.")]
public static partial Error AlreadyPaid(Guid orderId);
```

Nobody wrote `.Produces(409)`. The generator followed the handler into `IOrderService`, into its implementation, and into the private helper the implementation calls.

## What it looks like

Swagger UI, `POST /orders/{id}/pay` — five responses, each carrying its own codes, titles and example bodies. Note the `422`: two different failures share one status, and each keeps its own code.

![Swagger UI showing the pay endpoint with 200, 404, 409, 410 and 422 responses, each listing its error codes and example problem documents](https://raw.githubusercontent.com/SideswipeN7/ErrorApi/main/docs/images/swagger-pay-endpoint.png)

The same document in Scalar — every endpoint's failures visible without expanding anything.

![Scalar API reference listing all five endpoints, each with its error responses](https://raw.githubusercontent.com/SideswipeN7/ErrorApi/main/docs/images/scalar-overview.png)

Reproduce both with:

```bash
dotnet run --project samples/Sample.Api
```

`http://localhost:5080/swagger` · `http://localhost:5080/scalar` · `http://localhost:5080/openapi/v1.json` · `http://localhost:5080/openapi/errors.ts`

The same API is built three more times, once per result library, so the difference is only ever the
declaration style — the documents they produce are identical:

```bash
dotnet run --project samples/Sample.ErrorOr.Api      # :5081
```

```bash
dotnet run --project samples/Sample.OneOf.Api        # :5082
```

```bash
dotnet run --project samples/Sample.LanguageExt.Api  # :5083
```

```bash
dotnet run --project samples/Sample.Exceptions.Api   # :5084, no result type at all
```

```bash
dotnet run --project samples/Sample.Mediator.Api     # :5085, endpoints behind MediatR
```

```bash
dotnet run --project samples/Sample.FluentResults.Api  # :5086
```

```bash
dotnet run --project samples/Sample.Wolverine.Api    # :5088, convention handlers, no interfaces
```

One more runs the same pipeline through MediatR with a FluentValidation behaviour — and documents the
behaviour's 400 on every endpoint with no attribute anywhere. See
[pipeline behaviours are followed too](#pipeline-behaviours-are-followed-too).

```bash
dotnet run --project samples/Sample.Mediator.Validation.Api  # :5087
```

---

## Quickstart

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

The attribute carries the status and nothing else it does not have to: `NotFound` becomes the code
`Orders.NotFound` and the title `Not found`. [Codes you do not have to type](#codes-you-do-not-have-to-type)
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
builder.Services.AddErrorApi();
builder.Services.AddErrorApiExceptionHandler(o => o.UseExceptionMessageAsDetail = true);

var app = builder.Build();
app.UseExceptionHandler();
```

```json
{ "title": "Not found", "status": 404, "detail": "No order 1111….", "code": "Orders.NotFound" }
```

The body comes from the same `Error.ToProblem()` the result path uses, so a client cannot tell which
style the server was written in. An exception the catalog does not know is left untouched, so whatever
handled it before still does. `Exception.Message` stays off the wire unless you opt in with
`AddErrorApiExceptionHandler(o => o.UseExceptionMessageAsDetail = true)` — messages are written for
operators, and the entry's `Detail` is the documented place for client-facing text.

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

## Codes you do not have to type

`[Error(404)]` is enough. The wire code is resolved in this order:

1. the explicit argument, `[Error("Orders.NotFound", 404)]`;
2. a `code:` string literal in the member's own body — which is where ErrorOr and most factory-style
   error APIs already put it;
3. the declaration's name, prefixed by its catalog.

The prefix comes from `[ErrorCatalog("Orders")]` on the type or on the assembly. Without one, a member
takes its containing type's name with a trailing `Errors` removed — `OrderErrors.NotFound` yields
`Order.NotFound` — and an annotated type takes its own name unchanged.

The title defaults to the name read as a sentence: `AlreadyPaid` becomes `Already paid`. Set `Title`
when a better one exists, which for a catalog member it usually does.

Rule 2 is what collapses an ErrorOr catalog to a single attribute argument, and it removes a drift risk
along the way: the documented code and the code on the wire come from the same literal. Write the code
twice and let the two disagree, and `EAPI008` says so.

```csharp
public static class OrderErrors
{
    [ErrorApi.Error(404)]  // -> "Orders.NotFound", read out of the line below
    public static Error NotFound => Error.NotFound("Orders.NotFound", "No such order.");
}
```

A symbol from a referenced assembly has no body to read, so rule 2 cannot be re-applied by a consumer —
which is why the generator bakes every body-inferred resolution into the declaring assembly as
`[assembly: CatalogExport(...)]` and reads it back through the reference. The declaring assembly's
resolution is authoritative everywhere; nothing drifts, and nothing needs to be spelled twice.

---

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

language-ext errors carry a numeric code and a message, neither of which says anything about HTTP. Annotating your own `Expected` subclasses supplies the missing half.

```csharp
[ErrorApi.Error("Orders.NotFound", 404, Title = "Order not found")]
public sealed record OrderNotFound(Guid Id) : Expected("Order not found", 404);

public Fin<Order> GetById(Guid id) => new OrderNotFound(id);

orders.MapGet("/{id:guid}", (Guid id, IOrderService s) => s.GetById(id).ToHttpResult());
```

Resolution goes by type first, through a generated pattern switch. Failing that the numeric code is used when it already looks like an HTTP status; everything else becomes a 500.

> **Naming note.** ErrorOr and language-ext both ship a type called `Error`. When both namespaces are imported, spell ours out as `[ErrorApi.Error(...)]` or add `using ErrorAttribute = ErrorApi.ErrorAttribute;`.

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

`AddErrorApi()` publishes the model on `ErrorApiRuntime.Metadata`, which is what lets `result.ToHttpResult()` work as a plain extension method with no service provider in scope. Every adapter also has an overload taking `IErrorApiMetadata` explicitly, which is what the tests use.

---

## What the generator emits

| File | Contents |
| --- | --- |
| `<Catalog>.Catalog.g.cs` | The implementing half of every generated `[Error]` member, plus a `Codes` class of `const string` values you can use in `switch` patterns. |
| `ErrorApi.Metadata.g.cs` | The descriptor table, the endpoint→errors map, and the type→entry switch, all as `switch` statements over constants. No dictionary is built at startup, no type is scanned. |
| `ErrorApi.Registration.g.cs` | The zero-argument `AddErrorApi()` overload. Only emitted when the project references `ErrorApi.AspNetCore`, so a class library that holds only the catalog still builds. |

Read the real output for the sample app under `samples/Sample.Api/obj/generated/` after a build, or the approved snapshots in `tests/ErrorApi.Generator.Tests/Snapshots/`.

---

## How the errors are discovered

For each `MapGet`/`MapPost`/`MapPut`/`MapDelete`/`MapPatch`/`MapMethods`/`Map` call site the generator:

1. reads the route template — including any `MapGroup` prefixes, followed back through the local the group was assigned to and through intermediate calls like `.WithTags(...)`;
2. resolves the handler, whether it is a lambda or a method group;
3. walks the handler body, following every call into source — including **through interface and virtual dispatch**, by resolving the implementations present in the compilation — and records every `[Error]` member it reads and every `[Error]` type it sees constructed;
4. follows a message past a dispatcher it cannot read — see below.

Step 3 is what a runtime mapper cannot do. Endpoints normally talk to an `IOrderService`; the errors live two or three layers down.

### Endpoints behind a mediator

`sender.Send(new GetOrder(id))` used to end the walk. `ISender.Send` is implemented inside MediatR, so
there is nothing to step into — while the handler that actually raises the failures sits right there in
your compilation, just not reachable by following calls. The endpoint came out documented as having no
failures at all, which reads as deliberate.

The bridge is the message type. A handler is a source type implementing a generic interface constructed
with the message, which is the shape MediatR, Wolverine and Brighter all share — nothing in the
generator names a library:

```csharp
public sealed record GetOrder(Guid Id) : IRequest<Result<Order>>;

public sealed class GetOrderHandler(OrderStore store) : IRequestHandler<GetOrder, Result<Order>>
{
    public Task<Result<Order>> Handle(GetOrder request, CancellationToken ct) =>
        Task.FromResult(store.Find(request.Id));   // returns OrderErrors.NotFound
}

orders.MapGet("/{id:guid}", async (Guid id, ISender sender) =>
    (await sender.Send(new GetOrder(id))).ToHttpResult());   // documents 404
```

It deliberately over-matches a little: a validator declared as `IValidator<GetOrder>` matches too, and
walking it is right — its failures do reach that endpoint. Turn it off per project if the guess is wrong
for you:

```ini
[*.cs]
errorapi_follow_dispatch = false
```

When a dispatch cannot be resolved, `EAPI009` says so — whether the endpoint came out empty or merely
lost part of its contract, because a partial contract reads as complete and that is the worse failure.

### Pipeline behaviours are followed too

Following a message into its handler is not the same as following it through the whole pipeline. A
`ValidationBehaviour<TRequest, TResponse>` is generic over the request — that is the entire point of a
behaviour — so nothing in your source ever constructs it with a concrete message, and no call chain
leads to it. It is discovered by shape instead: crossing a dispatcher declared in assembly *M*, the
walk also enters every source type implementing a generic interface from *M* whose type arguments are
still type parameters. That matches behaviours exactly, and cannot match a handler — a handler closes
the interface with a concrete message, and walking it here would leak one endpoint's failures into
every other.

`samples/Sample.Mediator.Validation.Api` shows it end to end: a command, a FluentValidation validator,
a behaviour that throws an `[Error]`-annotated exception, handlers resolving their repository from a
scope of their own — and not a single `[ProducesError]`:

```
POST /orders                200 400 409
POST /orders/{id}/cancel    200 400 404 410
```

Two more things that sample demonstrates:

- **A separate scope is not a boundary.** Both handlers resolve `IOrderRepository` from a child
  container, and the 409, 404 and 410 those repositories return are all documented — the walk follows
  an interface to its implementation regardless of where the instance came from.
- **Convention handlers count.** A Wolverine-style `OrderHandler.Handle(PlaceOrder)` with no interface
  at all is found through the message too, by the `*Handler`/`*Consumer` suffix and the
  `Handle`/`Consume` method convention.

A cross-cutting failure can also be declared once, on the message that rides through the pipeline:

```csharp
[ProducesError("Common.RateLimited")]
public sealed record PlaceOrder(string Customer, decimal Total) : IRequest<Result<OrderPlaced>>;
```

Every endpoint that dispatches the message documents the code — no per-endpoint repetition.

When the walk cannot see the failure at all — it comes from a referenced assembly, or from a delegate built at runtime — declare it on the endpoint:

```csharp
orders.MapGet("/", [ProducesError("Common.RateLimited")] (IOrderService service) =>
        Results.Ok(Array.Empty<Order>()));
```

`[ProducesError]` also works on a method or a whole class, and is merged into every endpoint that reaches it.

### Errors nobody can return

A catalog entry that no endpoint reaches is reported as `EAPI010`, and it is worth knowing that this
has two very different causes:

- **The entry is dead.** Nothing raises it any more; delete it.
- **The contract lost it.** It is raised behind something the walk cannot follow — a generic pipeline
  behaviour, a handler in another assembly — so the endpoints that surface it never learned about it.
  The fix is `[ProducesError]` on those endpoints, not deleting the entry.

The second case is why the rule pays for itself: a contract that quietly lost half its failures shows
up here as codes nobody documents. `EAPI009` approaches the same hole from the other side — it fires on
any endpoint whose walk was stopped at a dispatcher, whether the contract came out empty or partial —
and the two together bracket the failure: one names the endpoint that lost something, the other names
what was lost.

A project with no endpoints is not an API, so a shared catalog library stays silent — put the catalog
in a project of its own and the rule has nothing to check it against.

### Diagnostics

| ID | Severity | Meaning |
| --- | --- | --- |
| `EAPI001` | Error | The same code is declared twice. |
| `EAPI002` | Warning | The route template is not a compile-time constant, so the endpoint cannot be documented. |
| `EAPI003` | Error | A `partial` catalog member is not `static`, does not return `Error`, or sits in a non-`partial` type; or `[Error]` is on an abstract or static type. |
| `EAPI004` | Error | The status code is outside 100–599. |
| `EAPI005` | Warning | `[ProducesError]` names a code that is not in the catalog. |
| `EAPI006` | Info | A handler returning `Result` reaches no catalog entry at all. |
| `EAPI007` | Warning | The handler could not be resolved to source. |
| `EAPI008` | Warning | An explicit code disagrees with the `code:` literal in the member's body. |
| `EAPI009` | Warning | The walk stopped at a dispatcher and the endpoint documents no failures. |
| `EAPI010` | Warning | A declared error is not returned by any endpoint in the project. |

Generator diagnostics are not suppressible with `#pragma`; tune them in `.editorconfig` or `<NoWarn>`.

---

## The TypeScript contract

```bash
dotnet run --project samples/Sample.Api -- --emit-error-contract ../client/api-errors.ts
```

```ts
export type ApiErrorCode = (typeof API_ERROR_CODES)[number];

export interface ApiProblem<TCode extends ApiErrorCode = ApiErrorCode> {
  code: TCode;
  status: number;
  title?: string;
  detail?: string;
}

/** Failures of `POST /orders/{id}/pay`. */
export type PostOrdersByIdPayError =
  | ApiProblem<"Orders.AlreadyPaid">
  | ApiProblem<"Orders.AmountMismatch">
  | ApiProblem<"Orders.Cancelled">
  | ApiProblem<"Orders.CurrencyMismatch">
  | ApiProblem<"Orders.NotFound">;
```

A `switch` over `problem.code` with an `assertNever` default now fails to compile the moment the API gains a failure mode the client does not handle. See `samples/client/orders-client.ts`.

The same module is served live at `/openapi/errors.ts` after `app.MapErrorContract()`, which suits a frontend build step better than a checked-in copy.

---

## Where this sits next to the alternatives

| | Failure typing | Maps to `ProblemDetails` | Documents errors in OpenAPI | Typed client errors |
| --- | --- | --- | --- | --- |
| **ErrorOr / FluentResults / OneOf** | yes | yes, at runtime | no | no |
| **Exceptions and a handler** | no | yes, at runtime | no | no |
| **NSwag / Kiota** | — | — | only what you hand-wrote with `.Produces(...)` | success shapes only |
| **`.Produces<ProblemDetails>(404)` by hand** | — | — | yes, until someone forgets | no |
| **ErrorApi** | yes, or bring your own | yes, generated | yes, derived from the code | yes, generated union |

The honest framing: ErrorOr solves the return type, NSwag solves the client shape, and neither answers *"which errors does this endpoint return?"* ErrorApi answers exactly that question and leans on the other two for everything else.

## Native AOT

Nothing in the runtime path uses reflection: the catalog is `const` data, the endpoint lookup is a `switch` over string literals, the type lookup is a pattern switch, and `Result → IResult` is a branch. Every package is marked `IsAotCompatible`; the sample builds with `PublishAot` enabled so the trim and AOT analyzers run over it in CI, not just in the README.

---

## Repository layout

```
src/ErrorApi.Abstractions   Error, Result<T>, [Error], [ProducesError], the metadata contracts
src/ErrorApi.Generator      the incremental generator (netstandard2.0, Roslyn 4.14)
src/ErrorApi.AspNetCore     Result→IResult mapping, the OpenAPI transformer, the TypeScript writer
src/ErrorApi.ErrorOr        adapter, pinned to ErrorOr 2.1.x
src/ErrorApi.OneOf          adapter, pinned to OneOf 3.0.x — and the entry point for hand-rolled unions
src/ErrorApi.LanguageExt    adapter, pinned to LanguageExt.Core 4.4.x
src/ErrorApi.FluentResults  adapter, pinned to FluentResults 3.16.x
tests/ErrorApi.TestKit      the generator harness, the snapshot assertion, a hand-built model
tests/…Generator.Tests      core tests — no result library referenced, so they pass on the core alone
tests/ErrorApi.*.Tests      one suite per adapter, each pinning its own library version
samples/Sample.Api          the reference API: route groups, interface dispatch, [ProducesError], AOT
samples/Sample.ErrorOr.Api  the same API in ErrorOr, with codes read out of the factory calls
samples/Sample.OneOf.Api    the same API as a union, with the failure cases carrying [Error]
samples/Sample.LanguageExt.Api  the same API in Fin<T>, with annotated Expected subclasses
samples/Sample.Exceptions.Api   the same API with no result type at all, only annotated exceptions
samples/Sample.Mediator.Api     the same API with every endpoint behind MediatR
samples/Sample.FluentResults.Api  the same API in FluentResults, with annotated Error subclasses
samples/Sample.Wolverine.Api    the same API behind Wolverine, handlers matched by convention
samples/Sample.Mediator.Validation.Api  MediatR + FluentValidation: the behaviour's 400 discovered on every endpoint
samples/client              how the generated union is consumed
```

Each adapter is its own package so you take only the dependency you already have. They share one generator: the compile-time half needs no per-library knowledge.

### Build and test

```bash
dotnet test ErrorApi.slnx
```

The generator tests compile real snippets against the live ASP.NET Core assemblies and compare the emitted files to approved snapshots under `tests/ErrorApi.Generator.Tests/Snapshots/`. To re-approve after an intentional change:

```bash
ERRORAPI_ACCEPT_SNAPSHOTS=1 dotnet test ErrorApi.slnx
```

Then read the diff. That diff is the review.

Each adapter has its own suite, referencing only its own library, so a version bump cannot leak into
anything else. The version under test is a build property, which is how one suite covers a range:

```bash
dotnet test tests/ErrorApi.ErrorOr.Tests -p:ErrorOrTestVersion=2.0.1
```

```bash
dotnet test tests/ErrorApi.OneOf.Tests -p:OneOfTestVersion=3.0.263
```

```bash
dotnet test tests/ErrorApi.LanguageExt.Tests -p:LanguageExtTestVersion=4.4.0
```

CI runs that as a matrix. The adapters are verified against ErrorOr 1.10.0 / 2.0.1 / 2.1.1,
OneOf 3.0.263 / 3.0.271, LanguageExt.Core 4.4.0 / 4.4.9 and FluentResults 3.15.0 / 3.16.0.

Working on this repository with a coding agent? [`AGENTS.md`](AGENTS.md) is the map: invariants, where each concern lives, and the checks that have to pass.

---

## Known limits

- Route templates must be compile-time constants (`EAPI002` tells you when they are not).
- Discovery follows source within the compilation. Errors raised inside a referenced assembly are found only when they flow through a call the generator can see, or when `[ProducesError]` declares them. `[assembly: ErrorMapping]` gives such a type a catalog entry, but not an endpoint.
- Interface dispatch resolves against the implementations *in the compilation*. A handler wired to an implementation that lives in another assembly needs `[ProducesError]`.
- Following a message past a dispatcher is a heuristic. It matches: a source type implementing a generic interface constructed with the message; a `*Handler`/`*Consumer` type with a `Handle`/`Consume` method taking the message (Wolverine's convention); and source types generic over the request implementing an interface from the dispatcher's assembly (pipeline behaviours). A handler resolved some other way — by name, by a registry — is still not found, and `EAPI009` reports it, on partial contracts too.
- Endpoints are matched by normalized route template plus HTTP method, so two endpoints that differ only by metadata (host, version header) share one entry.
- The call walk is bounded at a depth of 12 by default; an unusually layered application can raise it with `errorapi_walk_depth = 20` in `.editorconfig`.
