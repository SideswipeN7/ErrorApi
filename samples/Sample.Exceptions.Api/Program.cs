using ErrorApi.AspNetCore;
using Sample.Exceptions.Api.Orders;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

// Documents every endpoint's failures in OpenAPI.
builder.Services.AddErrorApi();

// Answers a thrown, annotated exception with the problem document its endpoint was documented with.
// Kept separate from AddErrorApi() on purpose: taking over exception handling is an explicit decision.
// The messages here are composed for clients, so putting them in `detail` is a deliberate opt-in.
builder.Services.AddErrorApiExceptionHandler(o => o.UseExceptionMessageAsDetail = true);

builder.Services.AddSingleton<IOrderService, InMemoryOrderService>();

var app = builder.Build();

if (app.TryEmitErrorContract(args))
{
    return;
}

app.UseExceptionHandler();

app.MapOpenApi();
app.MapErrorContract();
app.MapScalarApiReference("/scalar", options => options.WithTitle("Exceptions sample"));

var orders = app.MapGroup("/orders").WithTags("Orders");

// The handlers do not mention failure at all. Every documented response below comes from a `throw`
// the generator found in the service.
orders.MapGet("/{id:guid}", (Guid id, IOrderService service) => TypedResults.Ok(service.GetById(id)))
    .WithName("GetOrder")
    .WithSummary("Reads one order");

orders.MapPost("/", (CreateOrderRequest request, IOrderService service) =>
    {
        var order = service.Create(request);
        return TypedResults.CreatedAtRoute(order, "GetOrder", new RouteValueDictionary { ["id"] = order.Id });
    })
    .WithName("CreateOrder")
    .WithSummary("Creates an order");

orders.MapPost("/{id:guid}/pay", (Guid id, decimal amount, IOrderService service) =>
    {
        service.Pay(id, amount);
        return TypedResults.NoContent();
    })
    .WithName("PayOrder")
    .WithSummary("Pays an order in full");

app.Run();

/// <summary>The entry point, visible to WebApplicationFactory-based integration tests.</summary>
public partial class Program;
