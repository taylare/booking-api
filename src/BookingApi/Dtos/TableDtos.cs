namespace BookingApi.Dtos;

/// <summary>
/// A bookable table, as listed by GET /api/v1/tables.
/// </summary>
public record DiningTableResponse(int Id, string Name, int Capacity);

/// <summary>
/// The free slots for one table on one day.
/// </summary>
public record AvailabilityResponse(
    int DiningTableId,
    DateOnly Date,
    IReadOnlyList<TimeOnly> AvailableSlots);
