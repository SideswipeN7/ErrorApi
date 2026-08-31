<img src="../../docs/images/logo.svg" alt="ErrorApi" width="300">

# ErrorApi

**Start here. One package: the source generator, the `Error`/`Result` primitives and the ASP.NET Core
integration — so the OpenAPI document lists every failure each endpoint can actually return, and the
response body always matches it.**

```bash
dotnet add package ErrorApi
```

That is the whole install. This is a meta-package: it carries no code of its own, only
`ErrorApi.AspNetCore` (which ships the generator and the primitives) — and on .NET 8/9 also
`ErrorApi.Swashbuckle`, because there the document has to come through Swagger.

## Quickstart

**1. Declare the catalog** — the class declares membership and the default status, the members
declare the names; the generator writes the bodies and infers the codes:

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

**3. Wire it up once** — `AddErrorApi()` is the whole minimal setup:

```csharp
builder.Services.AddOpenApi();
builder.Services.AddErrorApi();

app.MapGet("/orders/{id:guid}", (Guid id, IOrderService s) => s.GetById(id).ToHttpResult());
```

On .NET 8/9 swap the first line for Swagger — the filter is already in the box:

```csharp
builder.Services.AddSwaggerGen(c => c.AddErrorApiResponses());
builder.Services.AddErrorApi();
```

One more thing there: partial *properties* like `NotFound` above are a C# 13 feature, so a net8/net9
project sets `<LangVersion>latest</LangVersion>` — or declares its entries as partial methods, which
every C# the packages support can compile.

The generator walks each handler through the call graph, works out which catalog entries it can
reach, and emits a reflection-free model. The document gains one response per status with every
reachable `code` listed and an example problem body; at runtime the same model maps each failure to
`application/problem+json` carrying that code — native-AOT clean, nothing decided on the request path.

## When to reach past this package

| Package | Take it when |
| --- | --- |
| `ErrorApi.Abstractions` | a class library only declares catalog entries — attributes and primitives, no ASP.NET Core |
| `ErrorApi.AspNetCore` | you want exactly the integration and nothing else picking dependencies for you |
| `ErrorApi.Swashbuckle` | a .NET 10 project stays on Swagger (on net8/net9 this package already brings it) |
| `ErrorApi.ErrorOr`, `.OneOf`, `.LanguageExt`, `.FluentResults`, `.ArdalisResult`, `.CSharpFunctionalExtensions` | you already use that result library — keep your types, often a bare `[Error]` is the entire onboarding |

## Full documentation

[github.com/SideswipeN7/ErrorApi](https://github.com/SideswipeN7/ErrorApi) — how discovery works, the
`EAPI001`–`EAPI013` diagnostics, the TypeScript error contract, and the result-library adapters.
