# Booking API

A small RESTful API for booking restaurant tables, built with C#, ASP.NET Core and PostgreSQL.
It is a portfolio demo with fake data, designed to show API design, database constraints,
concurrency handling, automated testing and documentation.

![CI](https://github.com/taylare/booking-api/actions/workflows/ci.yml/badge.svg)

**API docs (Swagger):** `https://YOUR-USERNAME.github.io/YOUR-REPO/`

## Tech stack

- C# on .NET 10 with ASP.NET Core minimal APIs
- Entity Framework Core with PostgreSQL (Npgsql)
- xUnit tests that run the real API against a real database
- GitHub Actions for CI, with a PostgreSQL service container
- OpenAPI document published as Swagger UI on GitHub Pages
- Docker Compose for one command local setup

## Endpoints

All routes are versioned under `/api/v1`. Errors use the standard problem details JSON format.

| Action | Request | Success | Errors |
|---|---|---|---|
| List tables | `GET /tables` | 200 | |
| Check availability | `GET /tables/{tableId}/availability?date=2030-01-15` | 200 | 400 missing or bad date, 404 unknown table |
| Create booking | `POST /bookings` | 201 with `Location` header | 400 invalid data, 409 slot taken |
| View booking | `GET /bookings/{id}` | 200 | 404 |
| List bookings | `GET /bookings?date=2030-01-15&page=1&pageSize=20` | 200 with paging | 400 bad paging |
| Cancel booking | `DELETE /bookings/{id}` | 204 | 404 |

Example request:

```json
POST /api/v1/bookings
{
  "diningTableId": 3,
  "bookingDate": "2030-01-15",
  "slotTime": "18:00:00",
  "partySize": 2,
  "customerName": "Alex Example",
  "customerEmail": "alex@example.com"
}
```

Slots are one hour long, on the hour, from 17:00 to 21:00.

## How double booking is prevented

Checking "is this slot free?" in code is not enough. If two requests arrive at the same moment,
both can see the slot as free and both can insert a booking. Instead, the database is the referee:

1. A **partial unique index** on `(DiningTableId, BookingDate, SlotTime)` where `Status = 'Confirmed'`
   means PostgreSQL itself rejects a second confirmed booking for the same slot.
2. The API catches that unique violation (PostgreSQL error `23505`) and returns **409 Conflict**.
3. Cancelling sets the status to `Cancelled`. The index ignores cancelled rows, so the slot can be rebooked.

The test `SimultaneousRequests_ForTheSameSlot_AllowExactlyOneBooking` sends 20 requests at the same
instant and asserts that exactly one returns 201 and the other 19 return 409. The schema and index
are also in [`db/schema.sql`](db/schema.sql) so you can try the race directly in `psql`.

## Run it locally

With Docker (no .NET install needed):

```bash
cp .env.example .env        # then edit .env and choose your own password
docker compose up --build
# API is now at http://localhost:8080, OpenAPI document at /openapi/v1.json
```

With the .NET SDK and your own PostgreSQL:

```bash
# Point the API at your database (the default in appsettings.json expects localhost)
export ConnectionStrings__BookingDb="Host=localhost;Port=5432;Database=booking;Username=booking;Password=change_me"
dotnet run --project src/BookingApi
```

## Run the tests

The tests start the real API in memory and talk to a real PostgreSQL database.

```bash
export TEST_DB_CONNECTION="Host=localhost;Port=5432;Database=booking_test;Username=booking;Password=change_me"
dotnet test tests/BookingApi.Tests
```

## Continuous integration

`.github/workflows/ci.yml` does the following:

- On every push and pull request: builds the code and runs all tests against a temporary PostgreSQL service container.
- On pushes to `main`: starts the API, downloads its OpenAPI document and publishes it as Swagger UI to GitHub Pages.
  One-time setup: repository Settings > Pages > Source: **GitHub Actions**.

## Design decisions and limitations

- **Fixed hourly slots** keep clashes exact, so a unique index can enforce them. Overlapping time ranges would need a PostgreSQL exclusion constraint.
- **Times are the restaurant's local time**, stored as a date and a time with no time zone conversion. A multi-location system would need per location time zones.
- **Customer details live on the booking** to keep the demo small. A fuller design would add a customers table.
- **`EnsureCreated` builds the schema** on startup for easy setup. A production system would use EF Core migrations.
- **No authentication**, because this is a public demo with fake data. Possible next steps: JWT login, rate limiting, confirmation emails, rescheduling.

## Project structure

```
src/BookingApi/
  Models/        Database entities and booking rules
  Data/          EF Core context and startup schema creation
  Dtos/          Request and response shapes
  Validation/    Business rule checks for new bookings
  Endpoints/     Table and booking routes
  Program.cs     Application wiring
tests/BookingApi.Tests/   End to end tests including the concurrency test
db/schema.sql             Reference copy of the database schema
docs/index.html           Swagger UI page published to GitHub Pages
.github/workflows/ci.yml  Build, test and docs pipeline
```

## License

MIT
