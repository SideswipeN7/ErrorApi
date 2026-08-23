using ErrorApi;

namespace Sample.Api.Orders;

/// <summary>Errors shared by several features. Catalogs can be split freely; codes stay globally unique.</summary>
[ErrorCatalog("Common")]
public static partial class CommonErrors
{
    [Error(StatusCodes.Status400BadRequest,
        Title = "Request failed validation",
        Detail = "{0}")]
    public static partial Error Validation(string message);

    [Error(StatusCodes.Status429TooManyRequests,
        Title = "Too many requests",
        Description = "The caller exceeded the per-minute quota for this endpoint.")]
    public static partial Error RateLimited { get; }
}
