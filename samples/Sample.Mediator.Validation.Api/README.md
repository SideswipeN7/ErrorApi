# Sample.Mediator.Validation.Api

MediatR + FluentValidation — the sample that proves **pipeline behaviours** are followed: the
validation behaviour is generic over the request and closed only at runtime, which is exactly why
following the message could never reach it. The walk finds it as a source type generic over the
request implementing an interface from the dispatcher''s assembly. Port **:5087**.

## The shape

```csharp
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(...) =>
        /* failures become CommonErrors.Validation - a 400 every request can hit */
}
```

## What was added, in order

1. The MediatR sample''s setup, plus FluentValidation validators and the pipeline behaviour.
2. `[Error(400)]` on the validation failure the behaviour raises.
3. `builder.Services.AddErrorApi();` — nothing else; no attribute on any endpoint.
4. `app.MapOpenApi(); app.MapErrorContract();`

## What Swagger shows

**Every** endpoint behind the dispatcher documents the behaviour''s 400 alongside its own failures —
the pipeline rides under each message, so each endpoint''s contract carries it. Validators declared as
`IValidator<TMessage>` are walked too: their failures reach the endpoint that dispatches the message.

```bash
dotnet run --project samples/Sample.Mediator.Validation.Api
```

Then: `http://localhost:5087/swagger` · `/scalar` · `/openapi/errors.ts`.
