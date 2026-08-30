using ErrorApi;
using ErrorApi.AspNetCore;
using MediatR;
using Sample.Mediator.Api.Orders;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddErrorApi();

builder.Services.AddMediatR(c => c.RegisterServicesFromAssemblyContaining<GetOrder>());
builder.Services.AddSingleton<OrderStore>();

var app = builder.Build();

if (app.TryEmitErrorContract(args))
{
    return;
}

app.MapOpenApi();
app.MapErrorContract();
app.MapScalarApiReference("/scalar", options => options.WithTitle("Mediator sample"));

// AddErrorApiResults: endpoints under this group may return Result/Result<T> directly — the filter
// maps success and failure exactly as ToHttpResult() would, and rewrites the 200 metadata to T.
var orders = app.MapGroup("/orders").WithTags("Orders").AddErrorApiResults();

// Nothing here mentions a failure, and the handlers are not even reachable by following calls:
// ISender.Send is implemented inside MediatR. The generator bridges it through the message type,
// finds the IRequestHandler<,> for each request, and documents what those handlers raise.
// No mapping call either: the Result<Order> is returned as-is and the group's filter answers.
orders.MapGet("/{id:guid}", async (Guid id, ISender sender) =>
        await sender.Send(new GetOrder(id)))
    .WithName("GetOrder")
    .WithSummary("Reads one order");

orders.MapPost("/", async (CreateOrder command, ISender sender) =>
        (await sender.Send(command)).ToCreatedAtRoute("GetOrder", order => new() { ["id"] = order.Id }))
    .WithName("CreateOrder")
    .WithSummary("Creates an order");

orders.MapPost("/{id:guid}/pay", async (Guid id, decimal amount, ISender sender) =>
        (await sender.Send(new PayOrder(id, amount))).ToHttpResult())
    .WithName("PayOrder")
    .WithSummary("Pays an order in full");

app.Run();

/// <summary>The entry point, visible to WebApplicationFactory-based integration tests.</summary>
public partial class Program;
