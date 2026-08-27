using ErrorApi.AspNetCore;
using Sample.Controllers.Api.Orders;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddErrorApi();
builder.Services.AddSingleton<IOrderStore, InMemoryOrderStore>();

var app = builder.Build();

if (app.TryEmitErrorContract(args))
{
    return;
}

app.MapOpenApi();
app.MapErrorContract();
app.MapScalarApiReference("/scalar", options => options.WithTitle("Controllers sample"));
app.MapControllers();

app.Run();
