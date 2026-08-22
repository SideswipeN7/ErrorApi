using System.Text.Json.Serialization;
using ErrorApi.AspNetCore;
using Microsoft.AspNetCore.Mvc;
using Sample.Api.Orders;
using Scalar.AspNetCore;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, SampleJsonContext.Default));

builder.Services.AddOpenApi();

// Generated overload: registers this assembly's compile-time error model and documents every
// endpoint's failures in OpenAPI. There is no runtime scan behind it.
builder.Services.AddErrorApi();

builder.Services.AddSingleton<IOrderService, InMemoryOrderService>();

var app = builder.Build();

// `dotnet run -- --emit-error-contract ../client/src/api-errors.ts` writes the TypeScript union
// and exits, which is what a frontend build step wants.
if (app.TryEmitErrorContract(args))
{
    return;
}

app.MapOpenApi();
app.MapErrorContract();
app.MapOrderEndpoints();

// Two readers over the same document, so the generated error responses can be judged in both.
app.MapScalarApiReference("/scalar", options => options.WithTitle("Sample API — ErrorApi"));
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/openapi/v1.json", "Sample API v1");
    options.EnableDeepLinking();
    options.DefaultModelsExpandDepth(-1);
});

app.Run();

/// <summary>Serialization contract, so the sample stays native-AOT clean.</summary>
[JsonSerializable(typeof(Order))]
[JsonSerializable(typeof(Order[]))]
[JsonSerializable(typeof(CreateOrderRequest))]
[JsonSerializable(typeof(PayOrderRequest))]
[JsonSerializable(typeof(ProblemDetails))]
internal sealed partial class SampleJsonContext : JsonSerializerContext;
