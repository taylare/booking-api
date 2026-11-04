using BookingApi.Models;

namespace BookingApi.Dtos;

/// <summary>
/// A booking as returned to API clients.
/// A separate type from the database model, so the API contract can stay stable
/// even if the database tables change.
/// </summary>
public record BookingResponse(
    Guid Id,
    int DiningTableId,
    DateOnly BookingDate,
    TimeOnly SlotTime,
    int PartySize,
    string CustomerName,
    string CustomerEmail,
    string Status,
    DateTime CreatedAtUtc)
{
    // Copies a database booking into the shape the API returns
    public static BookingResponse FromBooking(Booking booking)
    {
        return new BookingResponse(
            booking.Id,
            booking.DiningTableId,
            booking.BookingDate,
            booking.SlotTime,
            booking.PartySize,
            booking.CustomerName,
            booking.CustomerEmail,
            booking.Status.ToString(),
            booking.CreatedAtUtc);
    }
}
