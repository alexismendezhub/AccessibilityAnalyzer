using System.Text.Json.Serialization;
using AccessibilityAnalyzer.Application;
using AccessibilityAnalyzer.Infrastructure;
using AccessibilityAnalyzer.Server.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseDefaultFiles();
app.MapStaticAssets();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.MapGet("/health", () => Results.Ok(new { status = "ok", product = "Accessibility Analyzer" }));
app.MapAccessibilityEndpoints();
app.MapFallback("/api/{**rest}", () => Results.NotFound());
app.MapFallbackToFile("/index.html");
app.Run();

public partial class Program;
