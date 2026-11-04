using BookingApi.Data;
using BookingApi.Endpoints;
using Microsoft.EntityFrameworkCore;

// Create the web application builder
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Register services with the dependency injection container

// The connection string is read when the database context is first needed, not here, so test
// and deployment settings (such as the ConnectionStrings__BookingDb environment variable) always win
builder.Services.AddDbContext<BookingDbContext>((serviceProvider, options) =>
{
    IConfiguration configuration = serviceProvider.GetRequiredService<IConfiguration>();
    string connectionString = configuration.GetConnectionString("BookingDb")
        ?? throw new InvalidOperationException("Connection string 'BookingDb' is not configured.");

    options.UseNpgsql(connectionString);
});

// A clock that endpoints receive by injection, so time can be controlled in tests
builder.Services.AddSingleton(TimeProvider.System);

// Standard "problem details" JSON for errors, for example {"title": "Slot already booked", "status": 409}
builder.Services.AddProblemDetails();

// Generates the OpenAPI document that describes every endpoint
builder.Services.AddOpenApi();

// Build the app and configure the request pipeline
WebApplication app = builder.Build();

// Turn unhandled errors into a clean problem details response instead of a stack trace
app.UseExceptionHandler();

// Give plain error responses (such as an unknown URL) the same problem details shape
app.UseStatusCodePages();

// Create the tables (and the demo data) if they do not exist yet
ILogger startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
await DatabaseInitializer.EnsureCreatedWithRetryAsync(app.Services, startupLogger);

// OpenAPI document served at /openapi/v1.json, used to build the Swagger docs site
app.MapOpenApi();

// All business endpoints share the /api/v1 prefix, so a future v2 can live alongside
app.MapGroup("/api/v1")
    .MapTableEndpoints()
    .MapBookingEndpoints();

// Start listening for requests
app.Run();

// Makes the Program class visible to the test project, which starts the whole API in memory
public partial class Program
{
}
