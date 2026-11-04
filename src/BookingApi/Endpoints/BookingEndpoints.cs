using System.Globalization;
using BookingApi.Data;
using BookingApi.Dtos;
using BookingApi.Models;
using BookingApi.Validation;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BookingApi.Endpoints;

/// <summary>
/// Create, view, list and cancel bookings.
/// </summary>
public static class BookingEndpoints
{
    // Paging limits and the shell. Every route lives under /api/v1/bookings.
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    public static RouteGroupBuilder MapBookingEndpoints(this RouteGroupBuilder api)
    {
        RouteGroupBuilder bookings = api.MapGroup("/bookings").WithTags("Bookings");

        // POST /api/v1/bookings, starting with the checks
        bookings.MapPost("/", async (CreateBookingRequest request, BookingDbContext database, TimeProvider clock) =>
            {
                // The table must exist before we can check the party size against its capacity
                DiningTable? table = await database.DiningTables
                    .AsNoTracking()
                    .FirstOrDefaultAsync(diningTable => diningTable.Id == request.DiningTableId);

                if (table is null)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        [nameof(request.DiningTableId)] = new[] { "Dining table does not exist." }
                    });
                }

                // "Today" comes from the injected clock, so tests can control time if they need to
                DateOnly today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
                Dictionary<string, string[]> validationErrors = BookingValidator.Validate(request, table, today);
                if (validationErrors.Count > 0)
                {
                    return Results.ValidationProblem(validationErrors);
                }

                // Friendly early check, so the common clash gets a clear 409 without a failed insert
                bool slotAlreadyTaken = await database.Bookings.AnyAsync(existingBooking =>
                    existingBooking.DiningTableId == request.DiningTableId
                    && existingBooking.BookingDate == request.BookingDate
                    && existingBooking.SlotTime == request.SlotTime
                    && existingBooking.Status == BookingStatus.Confirmed);

                if (slotAlreadyTaken)
                {
                    return SlotConflict(request);
                }

                var newBooking = new Booking
                {
                    Id = Guid.NewGuid(),
                    DiningTableId = request.DiningTableId,
                    BookingDate = request.BookingDate,
                    SlotTime = request.SlotTime,
                    PartySize = request.PartySize,
                    CustomerName = request.CustomerName.Trim(),
                    CustomerEmail = request.CustomerEmail,
                    Status = BookingStatus.Confirmed,
                    CreatedAtUtc = clock.GetUtcNow().UtcDateTime
                };

                database.Bookings.Add(newBooking);

                try
                {
                    await database.SaveChangesAsync();
                }
                catch (DbUpdateException exception) when (IsUniqueViolation(exception))
                {
                    // The early check above can pass for several requests at once. The unique index is
                    // the real guard: PostgreSQL lets exactly one insert through and rejects the rest.
                    return SlotConflict(request);
                }

                // 201 Created, with a Location header pointing at the new booking
                return Results.Created($"/api/v1/bookings/{newBooking.Id}", BookingResponse.FromBooking(newBooking));
            })
            .WithName("CreateBooking")
            .WithSummary("Create a booking for a table, day and slot")
            .WithDescription("Reserves one table for one hour. The slot must be on the hour between 17:00 and 21:00, the date cannot be in the past, and the party size must fit the table. Returns 201 with a Location header pointing at the new booking. If the table is already booked for that slot the API returns 409 Conflict, and the database guarantees this even when two requests arrive at the same moment.")
            .Produces<BookingResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        // GET /api/v1/bookings/{bookingId}
        bookings.MapGet("/{bookingId:guid}", async (Guid bookingId, BookingDbContext database) =>
            {
                Booking? foundBooking = await database.Bookings
                    .AsNoTracking()
                    .FirstOrDefaultAsync(existingBooking => existingBooking.Id == bookingId);

                return foundBooking is null
                    ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Booking not found")
                    : Results.Ok(BookingResponse.FromBooking(foundBooking));
            })
            .WithName("GetBooking")
            .WithSummary("Get one booking by id")
            .WithDescription("Returns a single booking, including its status (Confirmed or Cancelled). Returns 404 if the id does not exist.")
            .Produces<BookingResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // GET /api/v1/bookings?date=2026-12-01&page=1&pageSize=20
        bookings.MapGet("/", async (DateOnly? date, int? page, int? pageSize, BookingDbContext database) =>
            {
                int currentPage = page ?? 1;
                int currentPageSize = pageSize ?? DefaultPageSize;

                // Reject impossible paging values with a 400 instead of returning surprising results
                if (currentPage < 1 || currentPageSize < 1 || currentPageSize > MaxPageSize)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["paging"] = new[] { $"page must be 1 or more and pageSize must be between 1 and {MaxPageSize}." }
                    });
                }

                IQueryable<Booking> bookingQuery = database.Bookings.AsNoTracking();

                // The date filter is optional
                if (date is not null)
                {
                    bookingQuery = bookingQuery.Where(existingBooking => existingBooking.BookingDate == date.Value);
                }

                int totalCount = await bookingQuery.CountAsync();

                // A stable sort order is needed so pages never overlap or skip items
                List<Booking> bookingsOnPage = await bookingQuery
                    .OrderBy(existingBooking => existingBooking.BookingDate)
                    .ThenBy(existingBooking => existingBooking.SlotTime)
                    .ThenBy(existingBooking => existingBooking.Id)
                    .Skip((currentPage - 1) * currentPageSize)
                    .Take(currentPageSize)
                    .ToListAsync();

                List<BookingResponse> items = bookingsOnPage.Select(BookingResponse.FromBooking).ToList();

                return Results.Ok(new PagedResponse<BookingResponse>(items, currentPage, currentPageSize, totalCount));
            })
            .WithName("ListBookings")
            .WithSummary("List bookings, optionally for one day, with paging")
            .WithDescription("Returns bookings sorted by date and slot time. Pass date to see one day only. Results are paged: page starts at 1 and pageSize can be 1 to 100 (default 20). The response includes totalCount so a client knows how many pages exist.")
            .Produces<PagedResponse<BookingResponse>>(StatusCodes.Status200OK)
            .ProducesValidationProblem();

        // DELETE /api/v1/bookings/{bookingId} cancels the booking and frees its slot
        bookings.MapDelete("/{bookingId:guid}", async (Guid bookingId, BookingDbContext database) =>
            {
                Booking? foundBooking = await database.Bookings
                    .FirstOrDefaultAsync(existingBooking => existingBooking.Id == bookingId);

                if (foundBooking is null)
                {
                    return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Booking not found");
                }

                // Cancelling keeps the row for history. Safe to repeat, a second cancel changes nothing.
                if (foundBooking.Status != BookingStatus.Cancelled)
                {
                    foundBooking.Status = BookingStatus.Cancelled;
                    await database.SaveChangesAsync();
                }

                return Results.NoContent();
            })
            .WithName("CancelBooking")
            .WithSummary("Cancel a booking and free its slot")
            .WithDescription("Marks the booking as Cancelled and frees the table for that slot so it can be booked again. The record is kept for history (a soft delete). Safe to repeat: cancelling an already cancelled booking still returns 204. Returns 404 if the id does not exist.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return api;
    }

    // Helper methods

    // Builds the standard 409 Conflict response for a slot that is already booked
    private static IResult SlotConflict(CreateBookingRequest request)
    {
        string formattedDate = request.BookingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string formattedTime = request.SlotTime.ToString("HH:mm", CultureInfo.InvariantCulture);

        return Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Slot already booked",
            detail: $"Table {request.DiningTableId} is already booked on {formattedDate} at {formattedTime}.");
    }

    // True when PostgreSQL rejected the insert because of the unique index (error code 23505)
    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
    }
}
