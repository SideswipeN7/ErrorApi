using ErrorApi;
using ErrorApi.AspNetCore;
using FluentValidation;
using MediatR;
using Sample.MediatorValidation.Api.Orders;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddErrorApi();

// The exception the validation behaviour throws is an annotated type, so the handler answers it with
// the documented problem shape. The response is right even where the document is silent.
builder.Services.AddErrorApiExceptionHandler();

builder.Services.AddMediatR(c => c.RegisterServicesFromAssemblyContaining<PlaceOrder>());
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehaviour<,>));
builder.Services.AddScoped<IValidator<PlaceOrder>, PlaceOrderValidator>();
builder.Services.AddScoped<IValidator<CancelOrder>, CancelOrderValidator>();
builder.Services.AddScoped<IOrderRepository, InMemoryOrderRepository>();

var app = builder.Build();

if (app.TryEmitErrorContract(args))
{
    return;
}

app.UseExceptionHandler();

app.MapOpenApi();
app.MapErrorContract();
app.MapScalarApiReference("/scalar", options => options.WithTitle("Mediator + validation sample"));

var orders = app.MapGroup("/orders").WithTags("Orders");

// Two discovery paths meet here. Following the message: Send -> PlaceOrderHandler -> (its own scope)
// -> IOrderRepository -> InMemoryOrderRepository.Place -> Orders.DuplicateCustomer. And following the
// pipeline: crossing a dispatcher from MediatR's assembly, the walk also enters every source type
// implementing a MediatR interface that is still generic over the request — which is exactly
// ValidationBehaviour<,>, so the 400 it throws lands in this contract with no attribute anywhere.
orders.MapPost("/", async (PlaceOrder command, ISender sender) =>
        (await sender.Send(command)).ToCreatedAtRoute("GetOrder", placed => new() { ["id"] = placed.Id }))
    .WithName("PlaceOrder")
    .WithSummary("Places an order — 400 and 409 both discovered, no attribute");

// The same pipeline, and no [ProducesError] here either: both endpoints get the behaviour's 400 the
// same way, so the two contracts stay honest without a single manual declaration.
orders.MapPost("/{id:guid}/cancel",
        async (Guid id, CancelOrderBody body, ISender sender) =>
            (await sender.Send(new CancelOrder(id, body.Reason))).ToHttpResult())
    .WithName("CancelOrder")
    .WithSummary("Cancels an order — 400, 404 and 410 all discovered");

// Reading one back, so CreatedAtRoute has a target and the contract has a third shape.
orders.MapGet("/{id:guid}", (Guid id) => Results.Ok(new { id }))
    .WithName("GetOrder")
    .WithSummary("Reads one order");

app.Run();

/// <summary>Body of <c>POST /orders/{id}/cancel</c>.</summary>
public sealed record CancelOrderBody(string Reason);
