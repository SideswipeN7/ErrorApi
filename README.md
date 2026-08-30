<img src="https://raw.githubusercontent.com/SideswipeN7/ErrorApi/main/docs/images/logo.svg" alt="ErrorApi" width="340">

**A Roslyn source generator that turns a `Result<T>` error catalog into a Minimal API error mapper *and* an OpenAPI document that actually lists the failures — plus a TypeScript union for the client.**

Result libraries fixed the return type. They did not fix the contract: `ErrorOr`, `FluentResults` and friends map a failure to `ProblemDetails` at runtime, so the OpenAPI document still says `200 OK` and nothing else. The frontend has no idea a `409 Orders.AlreadyPaid` exists until it happens in production.

ErrorApi resolves that at compile time — and it does so whether you use its own `Result<T>`, ErrorOr, OneOf, language-ext, a hand-rolled discriminated union, or no result type at all: plain exceptions work too.

## What it looks like

Swagger UI, `POST /orders/{id}/pay` — five responses, each carrying its own codes, titles and example bodies. Note the `422`: two different failures share one status, and each keeps its own code.

![Swagger UI showing the pay endpoint with 200, 404, 409, 410 and 422 responses, each listing its error codes and example problem documents](https://raw.githubusercontent.com/SideswipeN7/ErrorApi/main/docs/images/swagger-pay-endpoint.png)

![Scalar API reference listing all five endpoints, each with its error responses](https://raw.githubusercontent.com/SideswipeN7/ErrorApi/main/docs/images/scalar-overview.png)

```bash
dotnet run --project samples/Sample.Api
```

`http://localhost:5080/swagger` · `/scalar` · `/openapi/v1.json` · `/openapi/errors.ts` — and the same
API is built once per declaration style under [`samples/`](samples), each with its own README, all
producing the identical contract.

## First steps

**1. Declare the catalog** — the class declares membership and the default status, the members declare
the names; nothing else needs typing:

```csharp
[ErrorCatalog("Orders", StatusCodes.Status404NotFound)]
public static partial class OrderErrors
{
    public static partial Error NotFound { get; }                              // Orders.NotFound, 404

    [Error(StatusCodes.Status409Conflict, Detail = "Order {0} was already paid.")]
    public static partial Error AlreadyPaid(Guid orderId);                     // Orders.AlreadyPaid, 409
}
```

**2. Return the entries** — `Error` converts implicitly into `Result<T>`:

```csharp
public Result<Order> GetById(Guid id) =>
    _orders.TryGetValue(id, out var order) ? order : OrderErrors.NotFound;
```

**3. Wire it up once** — `AddErrorApi()` is the whole minimal setup, and every knob is a lambda on it:

```csharp
builder.Services.AddOpenApi();
builder.Services.AddErrorApi();          // or AddErrorApi(x => x.AddExceptionHandler().Include(...))

app.MapGet("/orders/{id:guid}", (Guid id, IOrderService s) => s.GetById(id).ToHttpResult());
```

On .NET 8/9, or on any project that stays on Swagger, add `ErrorApi.Swashbuckle` and
`services.AddSwaggerGen(c => c.AddErrorApiResponses());` — the identical responses, built by the same
shared code.

Already on ErrorOr, OneOf, language-ext, FluentResults, Ardalis.Result or CSharpFunctionalExtensions?
Take the matching adapter package and keep your types — often a bare `[Error]` on what you already
wrote is the entire onboarding. See [docs/adapters.md](docs/adapters.md) and
[docs/getting-started.md](docs/getting-started.md).

## How it works

1. The generator reads your `[Error]`/`[ErrorCatalog]` declarations into a catalog — codes, statuses
   and titles inferred from what is already written (names, bodies, base constructors).
2. It finds every endpoint — Minimal API `Map*` call sites and attribute-routed controllers — and
   **walks each handler through the call graph**: into interfaces and their implementations, past
   mediators via the message type, into pipeline behaviours, and across assembly boundaries through
   baked-in exports.
3. What it cannot see, it says out loud: thirteen `EAPI` diagnostics report stopped walks, drifting
   codes and unreachable entries at build time, instead of letting the contract lie.
4. It emits a reflection-free model — switch statements, no runtime scan — that one OpenAPI
   transformer (or the Swashbuckle filter) and a TypeScript writer render from.
5. At runtime the same model maps every failure to `application/problem+json` carrying a stable
   `code` member, so the response always matches the document.

The full mechanics: [docs/discovery.md](docs/discovery.md) · [docs/catalog.md](docs/catalog.md) ·
[docs/typescript.md](docs/typescript.md).

## Why it is worth it

- **The contract stops lying.** Every reachable failure is documented per endpoint, and the response
  body always matches — same model on both sides.
- **Nothing is written twice.** Codes come from names or bodies, statuses from catalogs or base
  constructors; duplication is what the diagnostics hunt down, not what the library asks for.
- **Keep your result library.** Seven adapters produce byte-identical contracts; exceptions work too.
- **The client gets a compiler.** `errors.ts` turns `catch` folklore into an exhaustive union — add a
  failure server-side and the frontend build breaks instead of production.
- **Free at runtime.** No reflection on the request path, native-AOT clean, and measured: success
  costs the same as hand-written `TypedResults.Ok`.

ErrorOr solves the return type, NSwag solves the client shape — neither answers *"which errors does
this endpoint return?"*. ErrorApi answers exactly that question and leans on the others for the rest.

## Benchmarks

.NET 10, x64, BenchmarkDotNet — raw `TypedResults` as the floor:

| | Mean | Allocated |
| --- | ---: | ---: |
| Success path (any adapter) vs `TypedResults.Ok` | 4.2–5.9 ns vs 4.5 ns | 24 B vs 24 B |
| Generated lookups (`FindError`, type switch, route switch) | 2.5–5.8 ns | 0 B |
| Failure → `application/problem+json` | 60–81 ns | 304–328 B |

Full tables, methodology and the optimizations the first run bought:
[docs/performance.md](docs/performance.md) · [benchmarks/](benchmarks/ErrorApi.Benchmarks).

## Documentation

| | |
| --- | --- |
| [docs/getting-started.md](docs/getting-started.md) | the quickstart in detail, exceptions, package-owned failure types |
| [docs/catalog.md](docs/catalog.md) | declaring entries, inference rules, the "which attribute, when" table |
| [docs/adapters.md](docs/adapters.md) | ErrorOr, OneOf, language-ext, FluentResults, Ardalis, CFE — and version compatibility |
| [docs/discovery.md](docs/discovery.md) | the call-graph walk, boundaries, versioned routes, diagnostics, known limits |
| [docs/typescript.md](docs/typescript.md) | the generated client contract |
| [docs/performance.md](docs/performance.md) | benchmarks and native AOT |
| [docs/repository.md](docs/repository.md) | layout, build & test, how this sits next to the alternatives |
| [specs/](specs) | the feature specifications: requirements and their acceptance gates |
| [AGENTS.md](AGENTS.md) | the map for coding agents and contributors: invariants and the checks that must pass |

MIT licensed.
