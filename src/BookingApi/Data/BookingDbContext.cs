using BookingApi.Models;
using Microsoft.EntityFrameworkCore;

namespace BookingApi.Data;

/// <summary>
/// Entity Framework Core gateway to the PostgreSQL database.
/// </summary>
public class BookingDbContext : DbContext
{
    // Constructor and the two tables
    public BookingDbContext(DbContextOptions<BookingDbContext> options)
        : base(options)
    {
    }

    public DbSet<DiningTable> DiningTables => Set<DiningTable>();

    public DbSet<Booking> Bookings => Set<Booking>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Dining table configuration and seed data
        modelBuilder.Entity<DiningTable>(table =>
        {
            table.ToTable("dining_tables");
            table.HasKey(diningTable => diningTable.Id);
            table.Property(diningTable => diningTable.Name).HasMaxLength(50).IsRequired();

            // Fake demo data so the API works straight after the first start
            table.HasData(
                new DiningTable { Id = 1, Name = "Window 1", Capacity = 2 },
                new DiningTable { Id = 2, Name = "Window 2", Capacity = 2 },
                new DiningTable { Id = 3, Name = "Booth 1", Capacity = 4 },
                new DiningTable { Id = 4, Name = "Booth 2", Capacity = 4 },
                new DiningTable { Id = 5, Name = "Long table", Capacity = 8 });
        });

        // Booking configuration
        modelBuilder.Entity<Booking>(booking =>
        {
            booking.ToTable("bookings");
            booking.HasKey(existingBooking => existingBooking.Id);

            booking.Property(existingBooking => existingBooking.CustomerName).HasMaxLength(100).IsRequired();

            // 254 characters is the maximum length of a valid email address
            booking.Property(existingBooking => existingBooking.CustomerEmail).HasMaxLength(254).IsRequired();

            // Store the status as readable text ("Confirmed") instead of a number
            booking.Property(existingBooking => existingBooking.Status)
                .HasConversion<string>()
                .HasMaxLength(20);

            // Every booking belongs to one table. Restrict stops a table with bookings being deleted.
            booking.HasOne(existingBooking => existingBooking.DiningTable)
                .WithMany(diningTable => diningTable.Bookings)
                .HasForeignKey(existingBooking => existingBooking.DiningTableId)
                .OnDelete(DeleteBehavior.Restrict);

            // The double booking protection.
            // A unique index over table + date + time means PostgreSQL itself refuses a second
            // Confirmed booking for the same slot, even when two requests arrive at the same instant.
            // The filter makes it partial: cancelled bookings are ignored, so a cancelled slot can be rebooked.
            booking.HasIndex(existingBooking => new
                {
                    existingBooking.DiningTableId,
                    existingBooking.BookingDate,
                    existingBooking.SlotTime
                })
                .IsUnique()
                .HasFilter("\"Status\" = 'Confirmed'");
        });
    }
}
