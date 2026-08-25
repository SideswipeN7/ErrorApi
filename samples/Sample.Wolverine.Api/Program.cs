using ErrorApi;
using ErrorApi.AspNetCore;
using Sample.Wolverine.Api.Orders;
using Scalar.AspNetCore;
using Wolverine;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddErrorApi();
builder.Services.AddSingleton<OrderStore>();

// Wolverine 6 compiles its handler plumbing at runtime; the compiler moved to its own package.
builder.Host.UseWolverine(opts => opts.UseRuntimeCompilation());

var app = builder.Build();

if (app.TryEmitErrorContract(args))
{
    return;
}

app.MapOpenApi();
app.MapErrorContract();
app.MapScalarApiReference("/scalar", options => options.WithTitle("Wolverine sample"));

var orders = app.MapGroup("/orders").WithTags("Orders");

// Nothing here mentions a failure, and there is no handler interface to follow: Wolverine resolves
// `GetOrderHandler.Handle(GetOrder)` purely by convention, at runtime. The generator applies the same
// convention at compile time — `*Handler`/`*Consumer` with a `Handle`/`Consume` method taking the
// message — so each endpoint documents exactly what its handler can raise.
orders.MapGet("/{id:guid}", async (Guid id, IMessageBus bus) =>
        (await bus.InvokeAsync<Result<Order>>(new GetOrder(id))).ToHttpResult())
    .WithName("GetOrder")
    .WithSummary("Reads one order");

orders.MapPost("/", async (CreateOrder command, IMessageBus bus) =>
        (await bus.InvokeAsync<Result<Order>>(command)).ToCreatedAtRoute("GetOrder", order => new() { ["id"] = order.Id }))
    .WithName("CreateOrder")
    .WithSummary("Creates an order");

orders.MapPost("/{id:guid}/pay", async (Guid id, decimal amount, IMessageBus bus) =>
        (await bus.InvokeAsync<Result>(new PayOrder(id, amount))).ToHttpResult())
    .WithName("PayOrder")
    .WithSummary("Pays an order in full");

app.Run();
