using ErrorApi;

namespace Sample.Api.Orders;

/// <summary>
/// The order error catalog. Only the declarations live here; the generator writes the bodies, the
/// <c>Codes</c> constants, and the OpenAPI/TypeScript contract from the same attributes.
/// </summary>
/// <remarks>
/// The attribute carries the status and nothing else it does not have to. The wire code is built from
/// the catalog prefix and the member name — <c>NotFound</c> becomes <c>Orders.NotFound</c> — and the
/// title from the member name read as a sentence, which only needs spelling out when a better one exists.
/// </remarks>
[ErrorCatalog("Orders")]
public static partial class OrderErrors
{
    [Error(StatusCodes.Status404NotFound,
        Title = "Order not found",
        Description = "No order exists for the supplied identifier.")]
    public static partial Error NotFound { get; }

    [Error(StatusCodes.Status409Conflict,
        Title = "Order already paid",
        Detail = "Order {0} was already paid and cannot be paid again.",
        Description = "The order reached a terminal state before this request arrived.")]
    public static partial Error AlreadyPaid(Guid orderId);

    [Error(StatusCodes.Status422UnprocessableEntity,
        Title = "Payment amount does not match the order total",
        Detail = "Expected {0}, received {1}.")]
    public static partial Error AmountMismatch(decimal expected, decimal actual);

    [Error(StatusCodes.Status422UnprocessableEntity,
        Title = "Payment currency does not match the order",
        Detail = "Order is billed in {0}, the payment arrived in {1}.",
        Description = "Two different failures share status 422; each keeps its own code.")]
    public static partial Error CurrencyMismatch(string expected, string actual);

    // Nothing to add beyond the status: the code is Orders.Cancelled and the title reads "Cancelled".
    [Error(StatusCodes.Status410Gone,
        Description = "The order is gone and will not come back.")]
    public static partial Error Cancelled { get; }
}
