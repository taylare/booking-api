"use strict";

/*
 * Seated core: the demo data layer shared by the staff app (index.html)
 * and the guest booking page (book.html).
 *
 * Everything is stored in the browser (localStorage), so both pages see the
 * same bookings, guests and waitlist. The functions on `Seated.api` return
 * Promises on purpose: they mirror how the real API client behaves, so the
 * screens never need to change when the demo store is swapped for live calls.
 */
(function () {
  // Storage and domain constants
  const STORAGE_KEY = "seated-demo-v2";

  const TABLES = [
    { id: 1, name: "Window 1", capacity: 2 },
    { id: 2, name: "Window 2", capacity: 2 },
    { id: 3, name: "Booth 1", capacity: 4 },
    { id: 4, name: "Booth 2", capacity: 4 },
    { id: 5, name: "Long table", capacity: 8 }
  ];

  // One-hour sittings, on the hour, matching the C# BookingRules.AllSlots
  const SLOTS = ["17:00", "18:00", "19:00", "20:00", "21:00"];

  // Largest party the guest page offers (the biggest table seats 8)
  const MAX_PARTY_SIZE = Math.max(...TABLES.map((table) => table.capacity));

  // A booking holds its table for the slot while it is in one of these states.
  // NoShow and Cancelled free the table again.
  const SLOT_HOLDING_STATUSES = ["Confirmed", "Seated", "Completed"];

  // Which status a booking may move to from its current one
  const STATUS_TRANSITIONS = {
    Confirmed: ["Seated", "NoShow", "Cancelled"],
    Seated: ["Completed"],
    Completed: [],
    NoShow: [],
    Cancelled: []
  };

  // Tags staff can put on a guest profile
  const GUEST_TAGS = ["VIP", "Allergy", "Regular", "Celebration"];

  // Small utilities
  class ApiError extends Error {
    constructor(status, message) {
      super(message);
      this.status = status;
    }
  }

  function toISODate(date) {
    const month = String(date.getMonth() + 1).padStart(2, "0");
    const day = String(date.getDate()).padStart(2, "0");
    return date.getFullYear() + "-" + month + "-" + day;
  }

  function parseISODate(iso) {
    const [year, month, day] = iso.split("-").map(Number);
    return new Date(year, month - 1, day);
  }

  function todayISO() {
    return toISODate(new Date());
  }

  function addDays(iso, dayCount) {
    const date = parseISODate(iso);
    date.setDate(date.getDate() + dayCount);
    return toISODate(date);
  }

  function isPast(iso) {
    return iso < todayISO();
  }

  function isValidISODate(value) {
    return /^\d{4}-\d{2}-\d{2}$/.test(String(value || "")) && !Number.isNaN(parseISODate(value).getTime());
  }

  function uid() {
    try {
      if (crypto && crypto.randomUUID) {
        return crypto.randomUUID();
      }
    } catch (error) {
      // Fall through to the simple generator below
    }
    return "id-" + Math.random().toString(36).slice(2) + Date.now().toString(36);
  }

  function normaliseEmail(email) {
    return String(email || "").trim().toLowerCase();
  }

  function isValidEmail(email) {
    return /^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(email || "");
  }

  function escapeHtml(text) {
    return String(text).replace(/[&<>"']/g, (character) => ({
      "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;"
    }[character]));
  }

  const WEEKDAY_NAMES = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];
  const MONTH_NAMES = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

  function prettyDate(iso) {
    const date = parseISODate(iso);
    return {
      name: WEEKDAY_NAMES[date.getDay()],
      shortName: WEEKDAY_NAMES[date.getDay()].slice(0, 3),
      full: date.getDate() + " " + MONTH_NAMES[date.getMonth()] + " " + date.getFullYear(),
      short: date.getDate() + " " + MONTH_NAMES[date.getMonth()]
    };
  }

  function tableName(tableId) {
    const table = TABLES.find((candidate) => candidate.id === tableId);
    return table ? table.name : "Table " + tableId;
  }

  // Short code a guest can quote to find their booking, for example SEA-3F9A21
  function referenceFor(booking) {
    return "SEA-" + booking.id.replace(/-/g, "").slice(0, 6).toUpperCase();
  }

  function holdsSlot(booking) {
    return SLOT_HOLDING_STATUSES.includes(booking.status);
  }

  // Seeded random numbers, so the demo history looks the same on every first load
  function createRandom(seed) {
    let state = seed >>> 0;
    return function nextRandom() {
      state = (state + 0x6d2b79f5) >>> 0;
      let mixed = Math.imul(state ^ (state >>> 15), 1 | state);
      mixed = (mixed + Math.imul(mixed ^ (mixed >>> 7), 61 | mixed)) ^ mixed;
      return ((mixed ^ (mixed >>> 14)) >>> 0) / 4294967296;
    };
  }

  // Demo data
  const GUEST_ROSTER = [
    { name: "Ana Delgado", email: "ana.delgado@example.com" },
    { name: "Wei Chen", email: "wei.chen@example.com" },
    { name: "Marco Rivera", email: "marco.rivera@example.com" },
    { name: "Chidi Okafor", email: "chidi.okafor@example.com" },
    { name: "Layla Haddad", email: "layla.haddad@example.com" },
    { name: "Priya Patel", email: "priya.patel@example.com" },
    { name: "Tom Fraser", email: "tom.fraser@example.com" },
    { name: "Sofia Rossi", email: "sofia.rossi@example.com" },
    { name: "Kofi Mensah", email: "kofi.mensah@example.com" },
    { name: "Hana Tanaka", email: "hana.tanaka@example.com" },
    { name: "Jan Kowalski", email: "jan.kowalski@example.com" },
    { name: "Linh Nguyen", email: "linh.nguyen@example.com" },
    { name: "Omar Said", email: "omar.said@example.com" },
    { name: "Grace Murphy", email: "grace.murphy@example.com" },
    { name: "Diego Alvarez", email: "diego.alvarez@example.com" },
    { name: "Mei Zhang", email: "mei.zhang@example.com" },
    { name: "Lucas Berg", email: "lucas.berg@example.com" },
    { name: "Aisha Khan", email: "aisha.khan@example.com" },
    { name: "Ben Carter", email: "ben.carter@example.com" },
    { name: "Elena Popov", email: "elena.popov@example.com" },
    { name: "Sam Okoye", email: "sam.okoye@example.com" },
    { name: "Yuki Sato", email: "yuki.sato@example.com" },
    { name: "Nora Lindqvist", email: "nora.lindqvist@example.com" },
    { name: "Raj Menon", email: "raj.menon@example.com" },
    { name: "Clara Dubois", email: "clara.dubois@example.com" },
    { name: "Ivan Petrov", email: "ivan.petrov@example.com" },
    { name: "Tess Whitaker", email: "tess.whitaker@example.com" },
    { name: "Malik Johnson", email: "malik.johnson@example.com" },
    { name: "Freya Olsen", email: "freya.olsen@example.com" },
    { name: "Dev Sharma", email: "dev.sharma@example.com" }
  ];

  // Profiles that start with tags and notes, so the guest screens have something to show
  const GUEST_PROFILE_SEEDS = {
    "wei.chen@example.com": {
      tags: ["VIP", "Celebration"],
      notes: ["Prefers a window table. Celebrates an anniversary here every November."]
    },
    "chidi.okafor@example.com": {
      tags: ["Allergy"],
      notes: ["Severe nut allergy. Tell the kitchen before the first course."]
    },
    "ana.delgado@example.com": {
      tags: ["Regular"],
      notes: ["Usually arrives ten minutes early and orders the tasting menu."]
    },
    "priya.patel@example.com": { tags: ["Regular"], notes: [] },
    "layla.haddad@example.com": {
      tags: [],
      notes: ["Books for large family groups. Needs a high chair."]
    }
  };

  function buildSeedState() {
    const today = todayISO();
    const random = createRandom(20261105);
    const seededState = { version: 2, bookings: [], guests: {}, waitlist: [] };

    GUEST_ROSTER.forEach((person) => {
      const profile = GUEST_PROFILE_SEEDS[person.email] || { tags: [], notes: [] };
      seededState.guests[person.email] = {
        email: person.email,
        name: person.name,
        tags: profile.tags.slice(),
        notes: profile.notes.map((text, noteIndex) => ({
          id: uid(),
          text: text,
          author: "Staff",
          createdAt: addDays(today, -40 + noteIndex) + "T10:00:00Z"
        }))
      };
    });

    function addSeedBooking(dayOffset, tableId, slotTime, partySize, person, status) {
      const bookingDate = addDays(today, dayOffset);
      seededState.bookings.push({
        id: uid(),
        diningTableId: tableId,
        bookingDate: bookingDate,
        slotTime: slotTime,
        partySize: partySize,
        customerName: person.name,
        customerEmail: person.email,
        status: status,
        source: "Staff",
        createdAtUtc: addDays(bookingDate, -3) + "T09:00:00Z"
      });
    }

    // Tonight, the same evening the earlier version of the demo showed
    addSeedBooking(0, 1, "17:00", 2, GUEST_ROSTER[0], "Seated");
    addSeedBooking(0, 2, "18:00", 2, GUEST_ROSTER[1], "Confirmed");
    addSeedBooking(0, 3, "19:00", 4, GUEST_ROSTER[2], "Confirmed");
    addSeedBooking(0, 5, "19:00", 7, GUEST_ROSTER[3], "Confirmed");
    addSeedBooking(0, 5, "20:00", 6, GUEST_ROSTER[4], "Confirmed");

    // How likely each sitting is to be busy, and a boost for Friday and Saturday
    const slotDemand = { "17:00": 0.35, "18:00": 0.55, "19:00": 0.8, "20:00": 0.65, "21:00": 0.3 };

    // Thirty days of history, so profiles have visit history and Insights has data
    for (let dayOffset = -30; dayOffset <= -1; dayOffset++) {
      const weekday = parseISODate(addDays(today, dayOffset)).getDay();
      const weekendBoost = weekday === 5 || weekday === 6 ? 0.15 : 0;

      SLOTS.forEach((slotTime) => {
        TABLES.forEach((table) => {
          if (random() > (slotDemand[slotTime] + weekendBoost) * 0.55) {
            return;
          }
          // Raising the random number to a power makes the first guests in the roster visit more often
          const person = GUEST_ROSTER[Math.floor(Math.pow(random(), 1.3) * GUEST_ROSTER.length)];
          const partySize = Math.max(1, table.capacity - Math.floor(random() * 3));
          const outcome = random();
          const status = outcome < 0.78 ? "Completed" : outcome < 0.88 ? "NoShow" : "Cancelled";
          addSeedBooking(dayOffset, table.id, slotTime, partySize, person, status);
        });
      });
    }

    // A few bookings in the days ahead
    for (let dayOffset = 1; dayOffset <= 5; dayOffset++) {
      SLOTS.forEach((slotTime) => {
        TABLES.forEach((table) => {
          if (random() > 0.14) {
            return;
          }
          const person = GUEST_ROSTER[Math.floor(random() * GUEST_ROSTER.length)];
          addSeedBooking(dayOffset, table.id, slotTime, Math.max(1, table.capacity - 1), person, "Confirmed");
        });
      });
    }

    // Two people already waiting for a table tonight
    seededState.waitlist.push(
      { id: uid(), date: today, preferredSlot: "19:00", partySize: 2, name: "Priya Patel", email: "priya.patel@example.com", createdAtUtc: new Date().toISOString() },
      { id: uid(), date: today, preferredSlot: null, partySize: 4, name: "Tom Fraser", email: "tom.fraser@example.com", createdAtUtc: new Date().toISOString() }
    );

    return seededState;
  }

  // Storage
  let state = loadState();

  function loadState() {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      if (raw) {
        const saved = JSON.parse(raw);
        if (saved && saved.version === 2) {
          return saved;
        }
      }
    } catch (error) {
      // Storage unavailable or corrupt: fall back to fresh demo data
    }
    const fresh = buildSeedState();
    persist(fresh);
    return fresh;
  }

  function persist(stateToSave) {
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(stateToSave));
    } catch (error) {
      // The demo still works for this visit even if the browser refuses to save
    }
  }

  function save() {
    persist(state);
  }

  // Re-read storage, used when the other page (staff or guest) changed it
  function reload() {
    state = loadState();
  }

  function resetDemo() {
    state = buildSeedState();
    save();
  }

  // Guests
  function ensureGuest(email, name) {
    const key = normaliseEmail(email);
    if (!state.guests[key]) {
      state.guests[key] = { email: key, name: String(name || "").trim(), tags: [], notes: [] };
    }
    return state.guests[key];
  }

  function summariseGuest(guest) {
    const today = todayISO();
    const history = state.bookings
      .filter((booking) => normaliseEmail(booking.customerEmail) === guest.email)
      .sort((first, second) => (second.bookingDate + second.slotTime).localeCompare(first.bookingDate + first.slotTime));

    const completedVisits = history.filter((booking) => booking.status === "Completed");
    return {
      guest: guest,
      history: history,
      visits: completedVisits.length,
      noShows: history.filter((booking) => booking.status === "NoShow").length,
      cancellations: history.filter((booking) => booking.status === "Cancelled").length,
      totalCovers: completedVisits.reduce((total, booking) => total + booking.partySize, 0),
      lastVisit: completedVisits.length ? completedVisits[0].bookingDate : null,
      upcoming: history.filter((booking) => booking.bookingDate >= today && ["Confirmed", "Seated"].includes(booking.status)).length
    };
  }

  // Tables and availability
  function findFreeTable(dateISO, slotTime, partySize) {
    const takenTableIds = state.bookings
      .filter((booking) => booking.bookingDate === dateISO && booking.slotTime === slotTime && holdsSlot(booking))
      .map((booking) => booking.diningTableId);

    // Smallest table that fits, so large tables stay free for large parties
    const candidates = TABLES
      .filter((table) => table.capacity >= partySize && !takenTableIds.includes(table.id))
      .sort((first, second) => first.capacity - second.capacity || first.id - second.id);
    return candidates.length ? candidates[0] : null;
  }

  // Booking validation, mirroring the rules in the C# BookingValidator
  function validateBookingRequest(request, table) {
    if (!isValidISODate(request.bookingDate)) {
      throw new ApiError(400, "Pick a valid date.");
    }
    if (isPast(request.bookingDate)) {
      throw new ApiError(400, "You cannot book a date in the past.");
    }
    if (!SLOTS.includes(request.slotTime)) {
      throw new ApiError(400, "Pick a valid one-hour slot.");
    }
    if (!Number.isInteger(request.partySize) || request.partySize < 1 || request.partySize > table.capacity) {
      throw new ApiError(400, "Party of " + request.partySize + " is too large for " + table.name + " (seats " + table.capacity + ").");
    }
    if (!request.customerName || request.customerName.trim().length === 0 || request.customerName.length > 100) {
      throw new ApiError(400, "A guest name is required.");
    }
    if (!isValidEmail(request.customerEmail)) {
      throw new ApiError(400, "Enter a valid email address.");
    }
  }

  // The API the screens use
  const api = {
    async getTables() {
      return TABLES.slice();
    },

    async listBookings(dateISO) {
      return state.bookings
        .filter((booking) => booking.bookingDate === dateISO)
        .sort((first, second) => first.slotTime.localeCompare(second.slotTime) || first.diningTableId - second.diningTableId);
    },

    async listAllBookings() {
      return state.bookings.slice();
    },

    // Which sittings still have a table that fits this party, for the guest page
    async getSlotAvailability(dateISO, partySize) {
      return SLOTS.map((slotTime) => ({
        slotTime: slotTime,
        available: findFreeTable(dateISO, slotTime, partySize) !== null
      }));
    },

    // Create a booking. Leave diningTableId empty to let Seated pick the smallest table that fits.
    async createBooking(request) {
      const wantsAutoTable = request.diningTableId === null || request.diningTableId === undefined;
      const table = wantsAutoTable
        ? findFreeTable(request.bookingDate, request.slotTime, request.partySize)
        : TABLES.find((candidate) => candidate.id === request.diningTableId);

      if (!table) {
        throw wantsAutoTable
          ? new ApiError(409, "No table is free for that party at that time.")
          : new ApiError(400, "That table does not exist.");
      }
      validateBookingRequest(request, table);

      const clash = state.bookings.some((booking) =>
        booking.diningTableId === table.id
        && booking.bookingDate === request.bookingDate
        && booking.slotTime === request.slotTime
        && holdsSlot(booking));
      if (clash) {
        throw new ApiError(409, "That slot was just taken.");
      }

      const booking = {
        id: uid(),
        diningTableId: table.id,
        bookingDate: request.bookingDate,
        slotTime: request.slotTime,
        partySize: request.partySize,
        customerName: request.customerName.trim(),
        customerEmail: normaliseEmail(request.customerEmail),
        status: "Confirmed",
        source: request.source || "Staff",
        createdAtUtc: new Date().toISOString()
      };
      state.bookings.push(booking);

      // Every booking creates or updates a guest profile, keyed by email
      const guest = ensureGuest(booking.customerEmail, booking.customerName);
      if (request.guestRequest && request.guestRequest.trim()) {
        guest.notes.push({ id: uid(), text: request.guestRequest.trim(), author: "Guest request", createdAt: new Date().toISOString() });
      }
      if (request.hasAllergy && !guest.tags.includes("Allergy")) {
        guest.tags.push("Allergy");
      }

      save();
      return booking;
    },

    // Move a booking along its life cycle: Confirmed to Seated to Completed, or to NoShow or Cancelled
    async setBookingStatus(bookingId, nextStatus) {
      const booking = state.bookings.find((candidate) => candidate.id === bookingId);
      if (!booking) {
        throw new ApiError(404, "Booking not found.");
      }
      if (booking.status === nextStatus) {
        return booking;
      }
      if (!STATUS_TRANSITIONS[booking.status].includes(nextStatus)) {
        throw new ApiError(409, "A " + booking.status.toLowerCase() + " booking cannot become " + nextStatus.toLowerCase() + ".");
      }
      booking.status = nextStatus;
      save();
      return booking;
    },

    async cancelBooking(bookingId) {
      return api.setBookingStatus(bookingId, "Cancelled");
    },

    // The guest page looks a booking up by its reference plus the email it was booked with
    async findBookingByReference(reference, email) {
      const wantedReference = String(reference || "").trim().toUpperCase();
      const wantedEmail = normaliseEmail(email);
      return state.bookings.find((booking) =>
        referenceFor(booking) === wantedReference && normaliseEmail(booking.customerEmail) === wantedEmail) || null;
    },

    // Guests
    async getGuestMap() {
      return state.guests;
    },

    async listGuests() {
      return Object.values(state.guests)
        .map(summariseGuest)
        .sort((first, second) => first.guest.name.localeCompare(second.guest.name));
    },

    async getGuest(email) {
      const guest = state.guests[normaliseEmail(email)];
      return guest ? summariseGuest(guest) : null;
    },

    async addGuestNote(email, text, author) {
      const trimmedText = String(text || "").trim();
      if (!trimmedText) {
        throw new ApiError(400, "Write something first.");
      }
      if (trimmedText.length > 500) {
        throw new ApiError(400, "Notes can be up to 500 characters.");
      }
      const guest = state.guests[normaliseEmail(email)];
      if (!guest) {
        throw new ApiError(404, "Guest not found.");
      }
      guest.notes.push({ id: uid(), text: trimmedText, author: author || "Staff", createdAt: new Date().toISOString() });
      save();
      return guest;
    },

    async deleteGuestNote(email, noteId) {
      const guest = state.guests[normaliseEmail(email)];
      if (guest) {
        guest.notes = guest.notes.filter((note) => note.id !== noteId);
        save();
      }
      return true;
    },

    async toggleGuestTag(email, tag) {
      const guest = state.guests[normaliseEmail(email)];
      if (!guest || !GUEST_TAGS.includes(tag)) {
        throw new ApiError(404, "Guest or tag not found.");
      }
      guest.tags = guest.tags.includes(tag) ? guest.tags.filter((existing) => existing !== tag) : guest.tags.concat(tag);
      save();
      return guest;
    },

    // Waitlist
    async listWaitlist(dateISO) {
      return state.waitlist
        .filter((entry) => entry.date === dateISO)
        .sort((first, second) => first.createdAtUtc.localeCompare(second.createdAtUtc));
    },

    async addToWaitlist(entry) {
      if (!isValidISODate(entry.date) || isPast(entry.date)) {
        throw new ApiError(400, "Pick today or a future date.");
      }
      if (!entry.name || !entry.name.trim()) {
        throw new ApiError(400, "A guest name is required.");
      }
      if (!isValidEmail(entry.email)) {
        throw new ApiError(400, "Enter a valid email address.");
      }
      if (!Number.isInteger(entry.partySize) || entry.partySize < 1 || entry.partySize > MAX_PARTY_SIZE) {
        throw new ApiError(400, "Party size must be between 1 and " + MAX_PARTY_SIZE + ".");
      }
      const waitlistEntry = {
        id: uid(),
        date: entry.date,
        preferredSlot: entry.preferredSlot || null,
        partySize: entry.partySize,
        name: entry.name.trim(),
        email: normaliseEmail(entry.email),
        createdAtUtc: new Date().toISOString()
      };
      state.waitlist.push(waitlistEntry);
      ensureGuest(waitlistEntry.email, waitlistEntry.name);
      save();
      return waitlistEntry;
    },

    async removeFromWaitlist(entryId) {
      state.waitlist = state.waitlist.filter((entry) => entry.id !== entryId);
      save();
      return true;
    },

    // Waitlist entries that could be seated right now, with the sitting that would work
    async findWaitlistMatches(dateISO) {
      if (isPast(dateISO)) {
        return [];
      }
      const matches = [];
      state.waitlist.filter((entry) => entry.date === dateISO).forEach((entry) => {
        const slotsToTry = entry.preferredSlot ? [entry.preferredSlot] : SLOTS;
        const workingSlot = slotsToTry.find((slotTime) => findFreeTable(dateISO, slotTime, entry.partySize));
        if (workingSlot) {
          matches.push({ entry: entry, slotTime: workingSlot });
        }
      });
      return matches;
    },

    // Turn a waitlist entry into a real booking, then remove it from the list
    async seatFromWaitlist(entryId) {
      const entry = state.waitlist.find((candidate) => candidate.id === entryId);
      if (!entry) {
        throw new ApiError(404, "Waitlist entry not found.");
      }
      const slotsToTry = entry.preferredSlot ? [entry.preferredSlot] : SLOTS;
      const workingSlot = slotsToTry.find((slotTime) => findFreeTable(entry.date, slotTime, entry.partySize));
      if (!workingSlot) {
        throw new ApiError(409, "No table fits this party yet.");
      }
      const booking = await api.createBooking({
        diningTableId: null,
        bookingDate: entry.date,
        slotTime: workingSlot,
        partySize: entry.partySize,
        customerName: entry.name,
        customerEmail: entry.email,
        source: "Waitlist"
      });
      state.waitlist = state.waitlist.filter((candidate) => candidate.id !== entryId);
      save();
      return booking;
    },

    // Insights, worked out from the bookings in the store
    async getInsights() {
      const today = todayISO();
      const coversOn = (dateISO) => state.bookings
        .filter((booking) => booking.bookingDate === dateISO && holdsSlot(booking))
        .reduce((total, booking) => total + booking.partySize, 0);

      // Covers for each of the last seven days, oldest first, plus the seven before for comparison
      const lastSevenDays = [];
      for (let dayOffset = -6; dayOffset <= 0; dayOffset++) {
        const dateISO = addDays(today, dayOffset);
        lastSevenDays.push({ dateISO: dateISO, label: prettyDate(dateISO).shortName, covers: coversOn(dateISO) });
      }
      let previousSevenDaysCovers = 0;
      for (let dayOffset = -13; dayOffset <= -7; dayOffset++) {
        previousSevenDaysCovers += coversOn(addDays(today, dayOffset));
      }
      const lastSevenDaysCovers = lastSevenDays.reduce((total, day) => total + day.covers, 0);

      // The last thirty days, used for rates and popularity
      const windowStart = addDays(today, -29);
      const windowBookings = state.bookings.filter((booking) => booking.bookingDate >= windowStart && booking.bookingDate <= today);
      const completedCount = windowBookings.filter((booking) => booking.status === "Completed").length;
      const noShowCount = windowBookings.filter((booking) => booking.status === "NoShow").length;

      const slotCounts = SLOTS.map((slotTime) => ({
        label: slotTime,
        value: windowBookings.filter((booking) => booking.slotTime === slotTime && holdsSlot(booking)).length
      }));
      const tableUtilisation = TABLES.map((table) => ({
        label: table.name,
        value: Math.round(windowBookings.filter((booking) => booking.diningTableId === table.id && holdsSlot(booking)).length / (30 * SLOTS.length) * 100)
      }));

      // Regulars: of everyone who has dined with us, how many have been at least five times
      const REGULAR_VISIT_COUNT = 5;
      const guestSummaries = Object.values(state.guests).map(summariseGuest);
      const diners = guestSummaries.filter((summary) => summary.visits >= 1).length;
      const regularDiners = guestSummaries.filter((summary) => summary.visits >= REGULAR_VISIT_COUNT).length;

      const busiestSlot = slotCounts.slice().sort((first, second) => second.value - first.value)[0];

      return {
        lastSevenDays: lastSevenDays,
        lastSevenDaysCovers: lastSevenDaysCovers,
        previousSevenDaysCovers: previousSevenDaysCovers,
        noShowRate: completedCount + noShowCount > 0 ? Math.round(noShowCount / (completedCount + noShowCount) * 100) : null,
        regularGuestRate: diners > 0 ? Math.round(regularDiners / diners * 100) : null,
        busiestSlot: busiestSlot && busiestSlot.value > 0 ? busiestSlot.label : null,
        slotCounts: slotCounts,
        tableUtilisation: tableUtilisation
      };
    }
  };

  window.Seated = {
    STORAGE_KEY, TABLES, SLOTS, MAX_PARTY_SIZE, GUEST_TAGS, STATUS_TRANSITIONS,
    ApiError, api, reload, resetDemo,
    toISODate, parseISODate, todayISO, addDays, isPast, prettyDate,
    tableName, referenceFor, holdsSlot, escapeHtml, normaliseEmail
  };
})();
