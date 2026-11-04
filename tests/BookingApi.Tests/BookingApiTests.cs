using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using BookingApi.Dtos;
using BookingApi.Models;

namespace BookingApi.Tests;

/// <summary>
/// End to end tests: each one sends real HTTP requests to the API, which talks to a real PostgreSQL database.
/// </summary>
public class BookingApiTests : IClassFixture<ApiFactory>
{
    // Shared setup and small helpers
    private const string BookingsUrl = "/api/v1/bookings";

    // Every test books on its own day, so tests never fight over the same slot.
    // The counter starts at a random day far in the future, so re-running the tests
    // against the same database does not collide with bookings left by earlier runs.
    private static int _nextDayOffset = Random.Shared.Next(60, 30000);

    private readonly HttpClient _client;

    public BookingApiTests(ApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    // Returns a day nobody has booked yet
    private static DateOnly NextTestDate()
    {
        int dayOffset = Interlocked.Increment(ref _nextDayOffset);
        return DateOnly.FromDateTime(DateTime.UtcNow).AddDays(dayOffset);
    }

    // Formats a date the way the API expects it in a URL, for example 2026-12-01
    private static string ToIsoDate(DateOnly date)
    {
        return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    // Builds a valid booking request. Table 3 is "Booth 1" and seats 4, so a party of 2 fits.
    private static CreateBookingRequest BuildRequest(
        DateOnly date,
        int tableId = 3,
        int partySize = 2,
        string customerName = "Test Guest")
    {
        return new CreateBookingRequest(
            tableId,
            date,
            new TimeOnly(18, 0),
            partySize,
            customerName,
            "guest@example.com");
    }

    // Availability and the happy path

    [Fact]
    public async Task Availability_ForUnbookedDay_ListsEverySlot()
    {
        DateOnly testDate = NextTestDate();

        HttpResponseMessage response = await _client.GetAsync(
            $"/api/v1/tables/3/availability?date={ToIsoDate(testDate)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AvailabilityResponse? availability = await response.Content.ReadFromJsonAsync<AvailabilityResponse>();
        Assert.NotNull(availability);
        Assert.Equal(BookingRules.SlotsPerDay, availability.AvailableSlots.Count);
    }

    [Fact]
    public async Task CreateBooking_WithValidRequest_ReturnsCreatedAndHoldsTheSlot()
    {
        DateOnly testDate = NextTestDate();
        CreateBookingRequest request = BuildRequest(testDate);

        HttpResponseMessage createResponse = await _client.PostAsJsonAsync(BookingsUrl, request);

        // 201 Created, with a Location header pointing at the new booking
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(createResponse.Headers.Location);
        BookingResponse? createdBooking = await createResponse.Content.ReadFromJsonAsync<BookingResponse>();
        Assert.NotNull(createdBooking);
        Assert.Equal("Confirmed", createdBooking.Status);

        // The booking can be fetched again by its id
        HttpResponseMessage getResponse = await _client.GetAsync($"{BookingsUrl}/{createdBooking.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        // The booked slot no longer appears as available
        AvailabilityResponse? availability = await _client.GetFromJsonAsync<AvailabilityResponse>(
            $"/api/v1/tables/3/availability?date={ToIsoDate(testDate)}");
        Assert.NotNull(availability);
        Assert.DoesNotContain(request.SlotTime, availability.AvailableSlots);
    }

    // Business rules and error responses

    [Fact]
    public async Task CreateBooking_ForSlotAlreadyBooked_ReturnsConflict()
    {
        DateOnly testDate = NextTestDate();

        HttpResponseMessage firstResponse = await _client.PostAsJsonAsync(BookingsUrl, BuildRequest(testDate));
        HttpResponseMessage secondResponse = await _client.PostAsJsonAsync(
            BookingsUrl,
            BuildRequest(testDate, customerName: "Second Guest"));

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
    }

    [Fact]
    public async Task CreateBooking_WithPartyLargerThanTable_ReturnsBadRequest()
    {
        // Table 1 is "Window 1" and seats only 2 people
        CreateBookingRequest request = BuildRequest(NextTestDate(), tableId: 1, partySize: 6);

        HttpResponseMessage response = await _client.PostAsJsonAsync(BookingsUrl, request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateBooking_ForPastDate_ReturnsBadRequest()
    {
        DateOnly yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);

        HttpResponseMessage response = await _client.PostAsJsonAsync(BookingsUrl, BuildRequest(yesterday));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetBooking_WithUnknownId_ReturnsNotFound()
    {
        HttpResponseMessage response = await _client.GetAsync($"{BookingsUrl}/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // Cancelling frees the slot

    [Fact]
    public async Task CancelBooking_FreesTheSlotForRebooking()
    {
        DateOnly testDate = NextTestDate();

        HttpResponseMessage createResponse = await _client.PostAsJsonAsync(BookingsUrl, BuildRequest(testDate));
        BookingResponse? createdBooking = await createResponse.Content.ReadFromJsonAsync<BookingResponse>();
        Assert.NotNull(createdBooking);

        // Cancel the booking: 204 No Content
        HttpResponseMessage cancelResponse = await _client.DeleteAsync($"{BookingsUrl}/{createdBooking.Id}");
        Assert.Equal(HttpStatusCode.NoContent, cancelResponse.StatusCode);

        // The cancelled booking is kept for history and reports its new status
        BookingResponse? cancelledBooking = await _client.GetFromJsonAsync<BookingResponse>(
            $"{BookingsUrl}/{createdBooking.Id}");
        Assert.NotNull(cancelledBooking);
        Assert.Equal("Cancelled", cancelledBooking.Status);

        // A different guest can now take the same slot
        HttpResponseMessage rebookResponse = await _client.PostAsJsonAsync(
            BookingsUrl,
            BuildRequest(testDate, customerName: "Second Guest"));
        Assert.Equal(HttpStatusCode.Created, rebookResponse.StatusCode);
    }

    // The showpiece. Many guests grab the same slot at the same instant.
    [Fact]
    public async Task SimultaneousRequests_ForTheSameSlot_AllowExactlyOneBooking()
    {
        const int simultaneousRequests = 20;
        DateOnly testDate = NextTestDate();

        // Every request waits at this gate, so they are all released to the server together
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        List<Task<HttpResponseMessage>> requestTasks = Enumerable
            .Range(0, simultaneousRequests)
            .Select(async guestNumber =>
            {
                await startGate.Task;
                return await _client.PostAsJsonAsync(
                    BookingsUrl,
                    BuildRequest(testDate, customerName: $"Guest {guestNumber}"));
            })
            .ToList();

        startGate.SetResult();
        HttpResponseMessage[] responses = await Task.WhenAll(requestTasks);

        int createdCount = responses.Count(response => response.StatusCode == HttpStatusCode.Created);
        int conflictCount = responses.Count(response => response.StatusCode == HttpStatusCode.Conflict);

        // Exactly one guest wins the slot and everyone else is told it is taken
        Assert.Equal(1, createdCount);
        Assert.Equal(simultaneousRequests - 1, conflictCount);

        // The database agrees: only one booking exists for that day
        PagedResponse<BookingResponse>? bookingsOnDay = await _client.GetFromJsonAsync<PagedResponse<BookingResponse>>(
            $"{BookingsUrl}?date={ToIsoDate(testDate)}");
        Assert.NotNull(bookingsOnDay);
        Assert.Equal(1, bookingsOnDay.TotalCount);
    }
}
