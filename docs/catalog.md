# The error catalog

Everything about declaring entries - and everything you never have to type, because the generator reads it from what is already written.

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

**The status doesn't have to be typed either.** A bare `[Error]` resolves its status in this order —
the most specific declaration always wins:

1. `[ErrorStatusCode(400)]` on the member — beats everything, including an explicit `[Error(404)]`;
2. the `[Error(404)]` argument itself;
3. the catalog's default: `[ErrorCatalog("Order.Validation", 422)]` gives every entry inside a status,
   so a catalog of same-status failures reads as one line per entry;
4. for an annotated **type**, an integer literal in its base constructor call — the shape of a library
   that already carries the status.

Rule 4 is what makes onboarding an existing result library nearly free. A language-ext catalog is
already written; `[Error]` just points at it:

```csharp
[ErrorCatalog("Orders")]
public static class OrderErrors
{
    [Error]   // -> "Orders.NotFound", 404, title "Order not found" — all read from the line below
    public sealed record NotFound(Guid Id) : Expected("Order not found", 404);
}
```

And the fast-catalog shape for a validation family — inside an `[ErrorCatalog]` type, a
`static partial Error` member **is** an entry, no `[Error]` needed at all; the class declares
membership, the members declare names:

```csharp
[ErrorCatalog("Order.Validation", 422)]
public static partial class ValidationErrors
{
    public static partial Error InvalidOrder { get; }                     // Order.Validation.InvalidOrder, 422
    public static partial Error MissingCustomer { get; }                  // Order.Validation.MissingCustomer, 422

    [ErrorStatusCode(400)]
    public static partial Error MalformedId { get; }                      // 400

    [ErrorDescription("The total must be positive.")]
    public static partial Error InvalidTotal { get; }                     // 422, with docs prose
}
```

A member that is not `partial` — a helper with its own body — is never claimed implicitly, and a
partial member you implement yourself stays your own; `[Error]` remains the explicit opt-in for both.

`[ErrorDescription]` is the documentation prose as its own attribute, so an entry that inherits
everything else still carries one line of docs; it overrides `[Error(Description = ...)]` the same way
`[ErrorStatusCode]` overrides the status.

A symbol from a referenced assembly has no body to read, so rule 2 for codes — and rule 4 for statuses —
cannot be re-applied by a consumer. The generator therefore bakes every source-inferred resolution into
the declaring assembly as `[assembly: CatalogExport(...)]` (code alone, or code + status + title) and
reads it back through the reference. The declaring assembly's resolution is authoritative everywhere;
nothing drifts, and nothing needs to be spelled twice.

### Which attribute, when

| You want | Write |
| --- | --- |
| The classic entry | `[Error(404)]` on a `static partial Error` member of an `[ErrorCatalog("Orders")]` class |
| An explicit wire code | `[Error("Orders.NotFound", 404)]` |
| A same-status family, one line per entry | `[ErrorCatalog("Order.Validation", 422)]` on the class — bare `static partial Error` members inside need **no attribute at all** |
| To adopt a library type that already carries the data | bare `[Error]` on the type — status and title read from the base constructor (`: Expected("msg", 404)`) |
| To override one entry | `[ErrorStatusCode(400)]` and/or `[ErrorDescription("…")]` — beats everything less specific (`EAPI013` tells you when the beaten declaration went stale) |
| A failure behind something the walk cannot cross | `[ProducesError("Orders.NotFound")]` or `[ProducesError(typeof(TimeoutException))]` — on the endpoint, the handler, or the message type |
| A catalog entry for a type nobody can annotate | `[assembly: ErrorMapping(typeof(TimeoutException), "Gateway.Timeout", 504)]` |
| To silence one diagnostic on one declaration | `[SuppressErrorApi("EAPI010")]` |

`CatalogExport`/`ReachabilityExport` are emitted by the generator, never written by hand.

---


---

[← back to the README](../README.md)
