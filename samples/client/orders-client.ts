// What the generated contract buys the frontend. Regenerate api-errors.ts with:
//   dotnet run --project ../Sample.Api -- --emit-error-contract ./api-errors.ts
//
// This file is illustrative and is not part of the build.

import {
  isApiProblem,
  type ApiErrorCode,
  type GetOrdersByIdError,
  type PostOrdersByIdPayError,
} from "./api-errors";

type Order = { id: string; customer: string; total: number; status: string };

async function getOrder(id: string): Promise<Order | GetOrdersByIdError> {
  const response = await fetch(`/orders/${id}`);
  const body: unknown = await response.json();

  if (!response.ok && isApiProblem(body)) {
    // Narrowed to exactly the failures GET /orders/{id} can produce.
    return body as GetOrdersByIdError;
  }

  return body as Order;
}

// The compiler enforces the match: add a new [Error] to the endpoint's reachable set and this
// switch stops compiling until the frontend handles it.
export function describePayFailure(problem: PostOrdersByIdPayError): string {
  switch (problem.code) {
    case "Orders.NotFound":
      return "That order no longer exists.";
    case "Orders.AlreadyPaid":
      return "This order has already been paid.";
    case "Orders.AmountMismatch":
      return problem.detail ?? "The amount does not match the order total.";
    case "Orders.Cancelled":
      return "This order was cancelled.";
    default:
      return assertNever(problem);
  }
}

function assertNever(value: never): never {
  throw new Error(`Unhandled error code: ${(value as { code: ApiErrorCode }).code}`);
}

export { getOrder };
