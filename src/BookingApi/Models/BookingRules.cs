namespace BookingApi.Models;

/// <summary>
/// Business rules for when bookings can be made.
/// Slots sit on a fixed grid so two bookings either clash exactly or not at all,
/// which lets the database enforce "no double booking" with a simple unique index.
/// </summary>
public static class BookingRules
{
    // The grid settings

    // First bookable slot of the evening
    public static readonly TimeOnly FirstSlotTime = new(17, 0);

    // Number of one hour slots per evening (17:00, 18:00, 19:00, 20:00, 21:00)
    public const int SlotsPerDay = 5;

    // Every valid slot start time, built once from the grid settings
    public static IReadOnlyList<TimeOnly> AllSlots { get; } = Enumerable
        .Range(0, SlotsPerDay)
        .Select(slotIndex => FirstSlotTime.Add(TimeSpan.FromHours(slotIndex)))
        .ToList();

    // True when the time is exactly on the slot grid
    public static bool IsValidSlot(TimeOnly slotTime)
    {
        return AllSlots.Contains(slotTime);
    }
}
