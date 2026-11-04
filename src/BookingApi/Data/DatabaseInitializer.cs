using Npgsql;

namespace BookingApi.Data;

/// <summary>
/// Creates the database schema on startup. For a production system this would be
/// replaced by EF Core migrations, but EnsureCreated keeps this demo easy to run.
/// </summary>
public static class DatabaseInitializer
{
    // Settings for how patiently we wait for the database

    // How many times to try before giving up
    private const int MaxAttempts = 10;

    // Pause between attempts, because the database container may still be starting
    private static readonly TimeSpan DelayBetweenAttempts = TimeSpan.FromSeconds(2);

    // Create the schema, retrying while the database is not yet reachable
    public static async Task EnsureCreatedWithRetryAsync(IServiceProvider services, ILogger logger)
    {
        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                // A scope gives us a short lived BookingDbContext, just like a web request would
                using IServiceScope scope = services.CreateScope();
                BookingDbContext database = scope.ServiceProvider.GetRequiredService<BookingDbContext>();

                await database.Database.EnsureCreatedAsync();
                return;
            }
            catch (NpgsqlException exception) when (attempt < MaxAttempts)
            {
                logger.LogWarning(
                    "Database not ready (attempt {Attempt} of {MaxAttempts}): {Message}",
                    attempt,
                    MaxAttempts,
                    exception.Message);

                await Task.Delay(DelayBetweenAttempts);
            }
        }
    }
}
