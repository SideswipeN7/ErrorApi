using ErrorApi.AspNetCore;
using ErrorApi.Interop;
using Sample.Cfe.Api.Orders;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

// Generated overload: registers this assembly's compile-time error model and documents every
// endpoint's failures in OpenAPI. There is no runtime scan behind it.
builder.Services.AddErrorApi();

builder.Services.AddSingleton<IOrderService, InMemoryOrderService>();

var app = builder.Build();

if (app.TryEmitErrorContract(args))
{
    return;
}

app.MapOpenApi();
app.MapErrorContract();
app.MapScalarApiReference("/scalar", options => options.WithTitle("CSharpFunctionalExtensions sample"));

var orders = app.MapGroup("/orders").WithTags("Orders");

// Nothing here mentions a failure: the generator follows each handler into IOrderService, into the
// implementation, and records every OrderError case it sees constructed.
orders.MapGet("/{id:guid}", (Guid id, IOrderService s) => s.GetById(id).ToHttpResult())
    .WithName("GetOrder")
    .WithSummary("Reads one order");

orders.MapPost("/", (CreateOrder request, IOrderService s) => s.Create(request).ToHttpResult())
    .WithName("CreateOrder")
    .WithSummary("Creates an order");

orders.MapPost("/{id:guid}/pay", (Guid id, decimal amount, IOrderService s) => s.Pay(id, amount).ToHttpResult())
    .WithName("PayOrder")
    .WithSummary("Pays an order in full");

app.Run();

/// <summary>The entry point, visible to WebApplicationFactory-based integration tests.</summary>
public partial class Program;
