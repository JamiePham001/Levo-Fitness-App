
using backend.Database;
using backend.Features.Auth;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Secrets come from user secrets locally (loaded in Development only), for example:
//   dotnet user-secrets set "SupaBase:ConnectionString" "<postgresql:// or Npgsql connection string>"
//   dotnet user-secrets set "SupaBase:ValidIssuers" "https://<project>.supabase.co/auth/v1"
var connectionString = builder.Configuration["SupaBase:ConnectionString"]
    ?? throw new InvalidOperationException("SupaBase:ConnectionString is not set.");
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
