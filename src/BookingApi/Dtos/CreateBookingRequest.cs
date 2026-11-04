namespace BookingApi.Dtos;

/// <summary>
/// The JSON body a client sends to create a booking.
/// </summary>
public record CreateBookingRequest(
    // Which table to book (see GET /api/v1/tables)
    int DiningTableId,

    // Day of the booking, written as 2026-12-01
    DateOnly BookingDate,

    // Slot start time on the hour between 17:00 and 21:00, written as 18:00:00
    TimeOnly SlotTime,

    // Number of guests, between 1 and the table capacity
    int PartySize,

    string CustomerName,

    string CustomerEmail);
