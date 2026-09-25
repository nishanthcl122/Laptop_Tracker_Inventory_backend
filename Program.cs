using System.Text.Json.Serialization;
using LaptopTracking.Api.Data;
using LaptopTracking.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add Database Context
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? "Server=.\\SQLEXPRESS;Database=LaptopTrackingDb;Trusted_Connection=True;TrustServerCertificate=True;";

builder.Services.AddDbContext<LaptopDbContext>(options =>
    options.UseSqlServer(connectionString));

// Register Domain Services
builder.Services.AddScoped<ExcelParserService>();
builder.Services.AddScoped<ComparisonEngine>();
builder.Services.AddScoped<ImportService>();
builder.Services.AddScoped<LaptopService>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<BatchService>();
builder.Services.AddScoped<RasReconciliationService>();
builder.Services.AddScoped<RasImportService>();

// Add Controllers with JSON formatting
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

// CORS for Frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Ensure Database schema is created on SQLEXPRESS
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LaptopDbContext>();
    try
    {
        db.Database.EnsureCreated();
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error initializing database.");
    }
}

app.UseCors("AllowAll");

app.MapControllers();

app.Run();
