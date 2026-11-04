using BookingApi.Data;
using BookingApi.Dtos;
using BookingApi.Models;
using Microsoft.EntityFrameworkCore;

namespace BookingApi.Endpoints;

/// <summary>
/// Read-only endpoints about tables and which slots are free.
/// </summary>
public static class TableEndpoints
{
    // The shell. Every route below lives under /api/v1/tables.
    public static RouteGroupBuilder MapTableEndpoints(this RouteGroupBuilder api)
    {
        RouteGroupBuilder tables = api.MapGroup("/tables").WithTags("Tables");

        // GET /api/v1/tables lists every table so clients can pick an id
        tables.MapGet("/", async (BookingDbContext database) =>
            {
                List<DiningTableResponse> allTables = await database.DiningTables
                    .AsNoTracking()
                    .OrderBy(diningTable => diningTable.Id)
                    .Select(diningTable => new DiningTableResponse(diningTable.Id, diningTable.Name, diningTable.Capacity))
                    .ToListAsync();

                return Results.Ok(allTables);
            })
            .WithName("ListTables")
            .WithSummary("List all dining tables")
            .WithDescription("Returns every table in the restaurant with its id, name and seat capacity. Call this first: the id is needed to check availability and to create a booking, and the capacity is the largest party the table accepts.")
            .Produces<List<DiningTableResponse>>(StatusCodes.Status200OK);

        // GET /api/v1/tables/{tableId}/availability?date=2026-12-01
        tables.MapGet("/{tableId:int}/availability", async (int tableId, DateOnly? date, BookingDbContext database) =>
            {
                // The date is mandatory, a missing one is a client mistake (400)
                if (date is null)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["date"] = new[] { "Query parameter 'date' is required, for example ?date=2026-12-01." }
                    });
                }

                bool tableExists = await database.DiningTables.AnyAsync(diningTable => diningTable.Id == tableId);
                if (!tableExists)
                {
                    return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Table not found");
                }

                // Only Confirmed bookings hold a slot, cancelled ones do not
                List<TimeOnly> bookedSlots = await database.Bookings
                    .AsNoTracking()
                    .Where(booking => booking.DiningTableId == tableId
                        && booking.BookingDate == date.Value
                        && booking.Status == BookingStatus.Confirmed)
                    .Select(booking => booking.SlotTime)
                    .ToListAsync();

                List<TimeOnly> availableSlots = BookingRules.AllSlots
                    .Where(slotTime => !bookedSlots.Contains(slotTime))
                    .ToList();

                return Results.Ok(new AvailabilityResponse(tableId, date.Value, availableSlots));
            })
            .WithName("GetAvailability")
            .WithSummary("List the free slots for a table on a given day")
            .WithDescription("Returns the hourly slots (17:00 to 21:00) that are still free for one table on one day. Cancelled bookings do not hold a slot. The date query parameter is required, and an unknown table returns 404.")
            .Produces<AvailabilityResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return api;
    }
}
