using ErrorApi;
using ErrorApi.AspNetCore;
using Sample.Shared.Errors;
using Sample.Toolbox.Api;
using Scalar.AspNetCore;

// A type shipped in a package nobody here can annotate: the mapping gives it a catalog entry, and
// [ProducesError(typeof(...))] below attaches it to the endpoint that surfaces it.
[assembly: ErrorMapping(typeof(TimeoutException), "Gateway.Timeout", 504, Title = "Upstream gateway timed out")]

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddErrorApi();

// TimeoutException is thrown, not returned, so the handler answers it with the documented problem shape.
builder.Services.AddErrorApiExceptionHandler();

builder.Services.AddSingleton<ICustomerService, CustomerService>();
builder.Services.AddSingleton<PromoteCustomerHandler>();
builder.Services.AddSingleton<IDispatcher, Dispatcher>();

var app = builder.Build();

if (app.TryEmitErrorContract(args))
{
    return;
}

app.UseExceptionHandler();

app.MapOpenApi();
app.MapErrorContract();
app.MapScalarApiReference("/scalar", options => options.WithTitle("Toolbox sample"));

var customers = app.MapGroup("/customers").WithTags("Customers");

// Everything this endpoint documents — the 404 and the body-inferred "Very.Old.Retired" 410 — comes
// from Sample.Shared.Errors, whose source this compilation never sees. The walk stops at
// ICustomerService.Find and continues through the ReachabilityExport baked into that assembly;
// the 410's code survives because the library exported its body-inferred resolution as CatalogExport.
customers.MapGet("/{id:guid}", (Guid id, ICustomerService service) => service.Find(id).ToHttpResult())
    .WithName("GetCustomer")
    .WithSummary("Reads one customer — 404 and 410 discovered across the assembly boundary");

// ToCreatedAtUri: the Uri twin of ToCreated, under its own name so a throwing or target-typed lambda
// can never be ambiguous between the string and Uri shapes.
customers.MapPost("/", (RegisterCustomer request, ICustomerService service) =>
        service.Register(request.Name).ToCreatedAtUri(customer => new Uri($"/customers/{customer.Id}", UriKind.Relative)))
    .WithName("RegisterCustomer")
    .WithSummary("Registers a customer — 409 discovered across the assembly boundary");

// The dispatch bridge across the boundary: IDispatcher's implementation is not in this compilation,
// so the message type is looked up in the referenced assembly's exports — 404, 409 and 410 all land
// here with no attribute anywhere.
customers.MapPost("/{id:guid}/promote", (Guid id, IDispatcher dispatcher) =>
        dispatcher.Send(new PromoteCustomer(id)).ToHttpResult())
    .WithName("PromoteCustomer")
    .WithSummary("Promotes a customer — the handler lives in the other assembly");

// A failure raised inside a library nobody annotated: declared by type, resolved through the
// assembly-level mapping, answered by the exception handler.
app.MapGet("/gateway/ping", [ProducesError(typeof(TimeoutException))] () =>
    {
        SimulatedGateway.Ping();
        return Results.Ok(new { status = "up" });
    })
    .WithTags("Gateway")
    .WithSummary("Pings the upstream gateway — 504 declared by type");

app.Run();

namespace Sample.Toolbox.Api
{
    /// <summary>Body of <c>POST /customers</c>.</summary>
    public sealed record RegisterCustomer(string Name);

    /// <summary>This API's own catalog: one entry, not wired up yet.</summary>
    [ErrorCatalog("Beta")]
    public static partial class BetaErrors
    {
        /// <summary>
        /// Kept for the throttling feature landing next release — no endpoint returns it yet, and that
        /// is deliberate, so EAPI010 is silenced on exactly this declaration instead of project-wide.
        /// </summary>
        [Error(429, Title = "Too many requests"), SuppressErrorApi("EAPI010")]
        public static partial Error Throttled { get; }
    }

    /// <summary>Stands in for an SDK that throws its own exception type.</summary>
    public static class SimulatedGateway
    {
        /// <summary>Succeeds most of the time; times out on the hour, like real gateways.</summary>
        public static void Ping()
        {
            if (DateTime.UtcNow.Minute == 0)
            {
                throw new TimeoutException("The upstream gateway did not answer within 2s.");
            }
        }
    }
}
