namespace BookingApi.Models;

/// <summary>
/// A guest's reservation of one dining table for one time slot.
/// Dates and times are the restaurant's local time, so no time zone conversion is needed.
/// </summary>
public class Booking
{
    // Identity and which table is booked
    public Guid Id { get; set; }

    public int DiningTableId { get; set; }

    // Navigation property to the booked table (loaded only when requested)
    public DiningTable? DiningTable { get; set; }

    // The day and the start time of the slot
    public DateOnly BookingDate { get; set; }

    public TimeOnly SlotTime { get; set; }

    // Number of guests, must not exceed the table capacity
    public int PartySize { get; set; }

    // Who the booking is for. Kept on the booking itself to keep this demo small.
    public string CustomerName { get; set; } = string.Empty;

    public string CustomerEmail { get; set; } = string.Empty;

    // Only Confirmed bookings hold the slot
    public BookingStatus Status { get; set; } = BookingStatus.Confirmed;

    // When the booking was created, always stored in UTC
    public DateTime CreatedAtUtc { get; set; }
}
