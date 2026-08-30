# How discovery works

What the generator emits, how the walk finds every reachable failure - through interfaces, mediators, pipelines and assembly boundaries - and where the honest limits are.

## What the generator emits

| File | Contents |
| --- | --- |
| `<Catalog>.Catalog.g.cs` | The implementing half of every generated `[Error]` member, plus a `Codes` class of `const string` values you can use in `switch` patterns. |
| `ErrorApi.Metadata.g.cs` | The descriptor table, the endpoint→errors map, and the type→entry switch, all as `switch` statements over constants. No dictionary is built at startup, no type is scanned. |
| `ErrorApi.Registration.g.cs` | The zero-argument `AddErrorApi()` overload. Only emitted when the project references `ErrorApi.AspNetCore`, so a class library that holds only the catalog still builds. |

Read the real output for the sample app under `samples/Sample.Api/obj/generated/` after a build, or the approved snapshots in `tests/ErrorApi.Generator.Tests/Snapshots/`.

---

---

## How the errors are discovered

For each `MapGet`/`MapPost`/`MapPut`/`MapDelete`/`MapPatch`/`MapMethods`/`Map` call site the generator:

1. reads the route template — including any `MapGroup` prefixes, followed back through the local the group was assigned to and through intermediate calls like `.WithTags(...)`;
2. resolves the handler, whether it is a lambda or a method group;
3. walks the handler body, following every call into source — including **through interface and virtual dispatch**, by resolving the implementations present in the compilation — and records every `[Error]` member it reads and every `[Error]` type it sees constructed;
4. follows a message past a dispatcher it cannot read — see below.

Step 3 is what a runtime mapper cannot do. Endpoints normally talk to an `IOrderService`; the errors live two or three layers down.

### Two versions of one route

Endpoint identity is route + method + **API description group**, so a header-versioned API that splits
its documents by group keeps a separate contract per version:

```csharp
app.MapGet("/orders/{id:guid}", V1Handler).WithGroupName("v1");   // documents 410 Orders.Retired
app.MapGet("/orders/{id:guid}", V2Handler).WithGroupName("v2");   // documents 404 Orders.NotFound
```

The group comes from `WithGroupName(...)` on the endpoint or its `MapGroup` chain, or
`[ApiExplorerSettings(GroupName = ...)]` on a controller or action. **Asp.Versioning literals count
too**: `MapToApiVersion(2)`, `HasApiVersion(new ApiVersion(1))` — on the endpoint, on the group
builder, or inside a version set built in a local — become the group the conventional `'v'VVV` format
produces (`v1`, `v1.1`). An endpoint carrying several versions stays ungrouped on purpose: it is one
handler with one contract in every document, and the fallback below serves them all.

Group names are matched through a small normalization (`EndpointGroup.Normalize`), so the compile-time
`v1` finds the runtime group whether your `GroupNameFormat` renders it as `v1`, `V1` or the default
`1.0`. Resolution at document-build time: the exact (normalized) group first, then the ungrouped entry,
and a null group also matches a route that lives in exactly one group — so a purely cosmetic
`WithGroupName` never hides an endpoint's errors. When two groups share a route, the TypeScript
contract tells them apart too: `GetOrdersByIdV1Error` / `GetOrdersByIdV2Error`, keyed as
`"GET /orders/{id} @v1"`.

When the same route is mapped more than once and *nothing* tells the mappings apart, the contracts
merge into one entry and `EAPI011` says so — because two API versions documented as one union is the
silent failure mode of versioning. If the mappings really are one contract, suppress it where it fires.

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

### Discovery across project boundaries

The walk reads source, so it used to stop at an assembly boundary: an `OrderService` implemented in
your Application project was invisible to the Api project's generator. Now the boundary carries the
knowledge across. A project that runs the generator and maps no endpoints is a library, and a library's
walk starts at its own public surface: every public method, every public property (reading one runs
its getter), and every message its handlers accept, gets its reachable codes baked in as

```csharp
[assembly: ReachabilityExport("M:App.IOrderService.GetById(System.Guid)", "Orders.NotFound")]
[assembly: ReachabilityExport("T:App.PayOrder", "Orders.AlreadyPaid")]
```

The consuming compilation reads these back through the reference and the walk continues as if the
boundary were not there — a direct call into the library resolves through the method entry, a
`sender.Send(new PayOrder(id))` whose handler lives in the library resolves through the message entry,
and the referenced catalog's `[Error]` attributes supply the full descriptors, so the documented
response is as rich as a same-assembly one. Exports compose transitively: each assembly's walk reads
the exports of the assemblies *it* references.

The dependency direction is the layered one, untouched: **`MyProject.Domain` knows nothing about
`MyProject.API`.** Domain and Application reference only `ErrorApi.Abstractions` and the generator;
each bakes its exports into its *own* assembly, and knowledge flows strictly along the references —
`Domain → Application → API` — because the export a project reads was computed while reading the
exports of the assemblies *it* references.

Nothing to configure — referencing an ErrorApi project is the whole setup — but both sides have an
explicit project-file knob when you want the trust spelled out:

```xml
<!-- MyProject.Domain.csproj — producer side: bake what my members can reach into my own assembly.
     Already the default for a project that maps no endpoints. -->
<ErrorApiExportReachability>true</ErrorApiExportReachability>

<!-- MyProject.API.csproj — consumer side: which referenced assemblies the walk may read exports and
     catalogs from. Unset means all references; exact names or a trailing-star prefix. -->
<ErrorApiIncludeAssemblies>MyProject.Domain;MyProject.Application</ErrorApiIncludeAssemblies>
```

`.editorconfig` works too (`errorapi_export_reachability = false`); the project file wins when both
are set.

The runtime has a composable twin. Every assembly that runs the generator exposes its model as
`<AssemblyName>.ErrorApiModel.Metadata`, and the API composes them explicitly:

```csharp
builder.Services.AddErrorApi(x => x.Include(
    MyProject.Domain.ErrorApiModel.Metadata,
    MyProject.Application.ErrorApiModel.Metadata));
```

The host's own model answers first; the included ones fill in what it cannot know — above all their
**instance-type switches**, so a failure whose type is declared in Domain resolves by instance in the
API process. `x.IncludeFromAssemblies(typeof(SomeDomainType).Assembly)` is the reflection convenience
of the same thing (startup-only; prefer `Include` under trimming or native AOT).

The same options object shapes what the model **documents** — without changing what the API does:

```csharp
builder.Services.AddErrorApi(x => x
    .ErrorCodeDescriptionEnabled(builder.Environment.IsDevelopment())  // prose off in production
    .HideErrorCodes("Orders.LegacyReplay")                              // or a predicate:
    .FilterErrorCodes(e => e.StatusCode < 500));
```

`ErrorCodeDescriptionEnabled(false)` strips the longer `Description` prose from the OpenAPI response
tables and examples and from the TypeScript contract's comments — codes, statuses and titles stay,
because they are the contract. The filters hide whole entries from the documented responses, the
catalog listing and the TS contract. Both are **documentation decisions only**: a hidden code still
resolves at runtime and endpoints answer exactly as before, so flipping them per environment can never
change behaviour. Several filters compose; an entry must pass all of them.

`x.AddExceptionHandler(...)` is the lambda form of `AddErrorApiExceptionHandler()`, so one
`AddErrorApi(x => ...)` call configures everything — still explicit, never a side effect. The
pipeline half stays yours: `app.UseExceptionHandler();` (with `AddProblemDetails()` registered) is
what makes the handler run.

A referenced assembly that does **not** run the generator has nothing to export, and stays a boundary —
`EAPI009` names it, `[ProducesError]` covers it. The library side has the same guard one boundary
earlier: when a library's *own* walk stops at a dispatcher it cannot see past, `EAPI012` reports that
the export it is baking is incomplete, instead of letting the consumer read it as complete.
`samples/Sample.Shared.Errors` + `samples/Sample.Toolbox.Api` show the whole round trip live,
body-inferred codes and both knobs included.

Calling `AddErrorApi()` twice — two registering modules in one host — keeps the **first** model, in DI
and on the ambient static alike; composing deliberately is what `x.Include(...)` is for.

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
| `EAPI011` | Warning | The same route is mapped more than once with no distinct API description groups, so the contracts merged into one. |
| `EAPI012` | Info | A reachability export stopped at a dispatcher; what this library bakes for its consumers is incomplete. |
| `EAPI013` | Info | `[Error]` and `[ErrorStatusCode]` declare different statuses on one entry; the override wins, but one of them is stale. |

Generator diagnostics are not suppressible with `#pragma`. For a deliberate one-off, silence the rule where it fires — `[SuppressErrorApi("EAPI010")]` on the member, or on the mapping method / handler for the endpoint rules — and keep `.editorconfig` / `<NoWarn>` for project-wide tuning.

---

---

## Known limits

- Route templates must be compile-time constants (`EAPI002` tells you when they are not).
- Discovery follows source within the compilation — plus the [reachability another ErrorApi project exported](#discovery-across-project-boundaries). A referenced assembly that does *not* run the generator stays opaque: its failures need `[ProducesError]`, and `[assembly: ErrorMapping]` gives such a type a catalog entry, but not an endpoint.
- Following a message past a dispatcher is a heuristic. It matches: a source type implementing a generic interface constructed with the message; a `*Handler`/`*Consumer` type with a `Handle`/`Consume` method taking the message (Wolverine's convention); and source types generic over the request implementing an interface from the dispatcher's assembly (pipeline behaviours). A handler resolved some other way — by name, by a registry — is still not found, and `EAPI009` reports it, on partial contracts too.
- Endpoints are matched by normalized route template, HTTP method and API description group — `WithGroupName(...)`, `[ApiExplorerSettings(GroupName = ...)]` or an Asp.Versioning literal (`MapToApiVersion`, `HasApiVersion`, a version set in a local) tells two versions of one route apart. A version computed at runtime is still invisible; when that leaves two mappings of one route indistinguishable, `EAPI011` reports the merge instead of letting it pass silently. Host-based routing (`RequireHost`) has no reflection in `ApiDescription` and still shares one entry.
- The call walk is bounded at a depth of 12 by default; an unusually layered application can raise it with `errorapi_walk_depth = 20` in `.editorconfig`.


---

[← back to the README](../README.md)
