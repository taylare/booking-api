namespace BookingApi.Models;

/// <summary>
/// Lifecycle state of a booking. Only Confirmed bookings hold a slot,
/// so cancelling a booking frees its slot for someone else.
/// </summary>
public enum BookingStatus
{
    /// <summary>The booking is active and the slot is reserved.</summary>
    Confirmed,

    /// <summary>The booking was cancelled and the slot is free again.</summary>
    Cancelled
}
