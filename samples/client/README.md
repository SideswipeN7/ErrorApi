# samples/client

How the generated TypeScript contract is consumed: `api-errors.ts` is emitted by
`dotnet run --project samples/Sample.Api -- --emit-error-contract src/api-errors.ts` (or fetched live
from `/openapi/errors.ts`), and the client switches on the `code` member with the compiler checking
exhaustiveness — an endpoint''s failures are a closed union, not a string.
