using System.Net.Mail;
using BookingApi.Dtos;
using BookingApi.Models;

namespace BookingApi.Validation;

/// <summary>
/// Checks a booking request against the business rules before it touches the database.
/// Returns a dictionary of field name to error messages, which ASP.NET turns into a 400 response.
/// </summary>
public static class BookingValidator
{
    // Entry point and the date and slot rules
    public static Dictionary<string, string[]> Validate(
        CreateBookingRequest request,
        DiningTable table,
        DateOnly today)
    {
        var errors = new Dictionary<string, string[]>();

        // Guests cannot book a day that has already passed
        if (request.BookingDate < today)
        {
            errors[nameof(request.BookingDate)] = new[] { "Booking date cannot be in the past." };
        }

        // The slot must sit exactly on the hourly grid, for example 18:00 but not 18:30
        if (!BookingRules.IsValidSlot(request.SlotTime))
        {
            errors[nameof(request.SlotTime)] = new[]
            {
                "Slot time must be on the hour between 17:00:00 and 21:00:00."
            };
        }

        // The party must fit at the chosen table
        if (request.PartySize < 1 || request.PartySize > table.Capacity)
        {
            errors[nameof(request.PartySize)] = new[]
            {
                $"Party size must be between 1 and {table.Capacity} for table '{table.Name}'."
            };
        }

        // Customer details. Limits match the database column sizes.
        if (string.IsNullOrWhiteSpace(request.CustomerName) || request.CustomerName.Length > 100)
        {
            errors[nameof(request.CustomerName)] = new[] { "Customer name is required and must be 100 characters or fewer." };
        }

        if (!IsValidEmail(request.CustomerEmail))
        {
            errors[nameof(request.CustomerEmail)] = new[] { "A valid email address is required." };
        }

        return errors;
    }

    // True when the text parses as an email address and nothing extra was attached,
    // for example "Name <a@b.com>" is rejected because only the bare address is wanted
    private static bool IsValidEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 254)
        {
            return false;
        }

        return MailAddress.TryCreate(email, out MailAddress? parsedAddress)
            && parsedAddress.Address == email;
    }
}
