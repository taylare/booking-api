namespace BookingApi.Models;

/// <summary>
/// A physical table in the restaurant that guests can book.
/// </summary>
public class DiningTable
{
    // Identity and descriptive fields
    public int Id { get; set; }

    // Friendly label shown to guests, for example "Window 1"
    public string Name { get; set; } = string.Empty;

    // The largest party this table can seat
    public int Capacity { get; set; }

    // All bookings ever made for this table, including cancelled ones
    public List<Booking> Bookings { get; set; } = new();
}
