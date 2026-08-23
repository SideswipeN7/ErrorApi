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

// What the generator finds here is exactly the path the code walks: Send -> PlaceOrderHandler ->
// (its own scope) -> IOrderRepository -> InMemoryOrderRepository.Place -> Orders.DuplicateCustomer.
// The 400 the validation behaviour raises is NOT here. MediatR closes ValidationBehaviour<,> at
// runtime from the container, so nothing in this source constructs it with PlaceOrder, and there is no
// call for the walk to follow. The endpoint answers 400 correctly and documents 409 only.
orders.MapPost("/", async (PlaceOrder command, ISender sender) =>
        (await sender.Send(command)).ToCreatedAtRoute("GetOrder", placed => new() { ["id"] = placed.Id }))
    .WithName("PlaceOrder")
    .WithSummary("Places an order — the validation failure is missing from this contract");

// The same pipeline, the same behaviour, one attribute. This is the fix available today: name the
// cross-cutting failure on the endpoint. Compare the two responses lists in the document.
orders.MapPost("/{id:guid}/cancel",
        [ProducesError("Common.Validation")] async (Guid id, CancelOrderBody body, ISender sender) =>
            (await sender.Send(new CancelOrder(id, body.Reason))).ToHttpResult())
    .WithName("CancelOrder")
    .WithSummary("Cancels an order — declares the validation failure explicitly");

// Reading one back, so CreatedAtRoute has a target and the contract has a third shape.
orders.MapGet("/{id:guid}", (Guid id) => Results.Ok(new { id }))
    .WithName("GetOrder")
    .WithSummary("Reads one order");

app.Run();

/// <summary>Body of <c>POST /orders/{id}/cancel</c>.</summary>
public sealed record CancelOrderBody(string Reason);
