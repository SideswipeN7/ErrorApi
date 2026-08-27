using ErrorApi.AspNetCore;
using ErrorApi.Interop;
using Sample.FluentResultsApi.Orders;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

// Generated overload: registers this assembly's compile-time error model and documents every
// endpoint's failures in OpenAPI. There is no runtime scan behind it.
builder.Services.AddErrorApi();

// A FluentResults result can carry several errors; the first decides the status and the code. Opting
// in lists the rest under the `errors` member — an optional field the schema and the TS ApiProblem
// type both document, so this stays inside the contract. POST /orders with an empty customer AND a
// non-positive total shows it live.
FluentResultsHttpExtensions.IncludeAllErrors = true;

builder.Services.AddSingleton<IOrderService, InMemoryOrderService>();

var app = builder.Build();

if (app.TryEmitErrorContract(args))
{
    return;
}

app.MapOpenApi();
app.MapErrorContract();
app.MapScalarApiReference("/scalar", options => options.WithTitle("FluentResults sample"));

var orders = app.MapGroup("/orders").WithTags("Orders");

orders.MapGet("/{id:guid}", (Guid id, IOrderService service) => service.GetById(id).ToHttpResult())
    .WithName("GetOrder")
    .WithSummary("Reads one order");

orders.MapPost("/", (CreateOrderRequest request, IOrderService service) =>
        service.Create(request).ToCreatedAtRoute("GetOrder", order => new() { ["id"] = order.Id }))
    .WithName("CreateOrder")
    .WithSummary("Creates an order");

orders.MapPost("/{id:guid}/pay", (Guid id, decimal amount, IOrderService service) =>
        service.Pay(id, amount).ToHttpResult())
    .WithName("PayOrder")
    .WithSummary("Pays an order in full");

app.Run();
