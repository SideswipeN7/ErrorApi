<img src="../../docs/images/logo.svg" alt="ErrorApi" width="300">

# ErrorApi.LanguageExt.V5

The [language-ext](https://github.com/louthy/language-ext) **v5** twin of `ErrorApi.LanguageExt`: the
same surface — `Fin<T>.ToHttpResult()`, `ToCreated`, `ToCreatedAtRoute`, `Either<Error, T>` — compiled
against the 5.x API, so every endpoint's OpenAPI document lists the failures it can actually return and
the wire response carries a stable machine-readable `code`.

```bash
dotnet add package ErrorApi.LanguageExt.V5 --prerelease
```

> **Prerelease, on purpose.** language-ext 5.0.0 has no stable release yet, and NuGet rightly refuses a
> stable package with a prerelease dependency. This package tracks the beta and goes stable the moment
> 5.0.0 does. On v4? Use `ErrorApi.LanguageExt` — the two packages are alternatives, never references
> of one project.

## Usage

Identical to the v4 adapter — annotate your own `Expected` subclasses, which is the idiomatic way to
model a domain failure in language-ext:

```csharp
[ErrorApi.Error("Orders.NotFound", 404, Title = "Order not found")]
public sealed record OrderNotFound(Guid Id) : Expected("Order not found", 404);

public Fin<Order> GetById(Guid id) =>
    _orders.TryGetValue(id, out var order) ? order : new OrderNotFound(id);

builder.Services.AddErrorApi();
orders.MapGet("/{id:guid}", (Guid id, IOrderService s) => s.GetById(id).ToHttpResult());
// Document: 200 + 404 with code "Orders.NotFound"; wire: the same problem body.
```

Resolution goes by type first, through the generated pattern switch. Failing that the numeric code is
used when it already looks like an HTTP status; everything else becomes a 500.

## Versions

Pinned to `LanguageExt.Core 5.0.0-beta-77`; the suite tracks newer betas via
`-p:LanguageExtV5TestVersion=5.0.0-beta-xx`.
