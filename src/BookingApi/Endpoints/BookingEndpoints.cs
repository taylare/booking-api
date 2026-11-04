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
            .Produces<BookingResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);
