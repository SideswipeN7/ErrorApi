# The TypeScript contract

The client half: one union per endpoint, generated from the same model the server compiled.

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


---

[← back to the README](../README.md)
