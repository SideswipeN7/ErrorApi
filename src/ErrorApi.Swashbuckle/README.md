<img src="../../docs/images/logo.svg" alt="ErrorApi" width="300">

# ErrorApi.Swashbuckle

**The Swagger road to ErrorApi documents — and the road for .NET 8/9, where the built-in OpenAPI
pipeline predates Microsoft.OpenApi 2.x.**

```bash
dotnet add package ErrorApi.Swashbuckle
```

ErrorApi works out at compile time which errors every endpoint can return. On .NET 10 the built-in
OpenAPI pipeline documents them via an operation transformer; this package does the identical job for
Swashbuckle documents — same responses, same schemas, same examples, because both compile the one
shared response builder. Use it when you are on net8/net9, or whenever your project stays on Swagger.

## Setup

```csharp
builder.Services.AddErrorApi();                              // the compile-time model, as always
builder.Services.AddSwaggerGen(c => c.AddErrorApiResponses());   // the document half, via Swashbuckle
```

That is the whole hookup. Every endpoint the generator matched gains its error responses:
`application/problem+json`, the `code` enum listing exactly the reachable codes, and one example per
code — byte-identical to what the .NET 10 transformer writes.

## What fills the responses

The same `IErrorApiMetadata` the rest of ErrorApi runs on: the generated, reflection-free model of
your catalog and endpoints. The filter resolves it from DI, looks the operation up by normalized
route + method + API description group, and writes one response per status.

## Full documentation

[github.com/SideswipeN7/ErrorApi](https://github.com/SideswipeN7/ErrorApi) — how discovery works, the
`EAPI001`–`EAPI013` diagnostics, the TypeScript contract, and the result-library adapters.
