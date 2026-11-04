using Microsoft.AspNetCore.Mvc.Testing;

namespace BookingApi.Tests;

/// <summary>
/// Starts the real booking API inside the test process.
/// Set the TEST_DB_CONNECTION environment variable to point the tests at a specific PostgreSQL database
/// (the CI workflow does this). Without it, the connection string from appsettings.json is used.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public ApiFactory()
    {
        string? testDatabaseConnection = Environment.GetEnvironmentVariable("TEST_DB_CONNECTION");

        if (!string.IsNullOrWhiteSpace(testDatabaseConnection))
        {
            // The API reads this variable when its host is built, which happens after this constructor runs
            Environment.SetEnvironmentVariable("ConnectionStrings__BookingDb", testDatabaseConnection);
        }
    }
}
