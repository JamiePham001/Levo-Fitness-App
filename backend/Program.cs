
using backend.Database;
using backend.Features.Auth;
using backend.Features.Sync;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// prod database
// var connectionString = builder.Configuration["SupaBase:ConnectionString"]
//     ?? throw new InvalidOperationException("SupaBase:ConnectionString is not set.");
// dev database
var connectionString = builder.Configuration["Local:ConnectionString"]
    ?? throw new InvalidOperationException("Local:ConnectionString is not set.");
builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(PostgresConnectionString.Normalize(connectionString)));

var validIssuers = builder.Configuration["SupaBase:ValidIssuers"]
    ?? throw new InvalidOperationException("SupaBase:ValidIssuers is not set.");
builder.Services.AddSupabaseAuth(validIssuers);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options => options.CustomSchemaIds(t => t.FullName?.Replace('+', '.')));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    // app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapSync();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast = Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");

app.Run();

sealed record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

// Lets integration tests start the app with WebApplicationFactory<Program>.
public partial class Program;
