using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using TravelAgency.Helpers;
using TravelAgency.ViewModel;

namespace TravelAgency.Controllers;

public class BookingController : Controller
{
    private readonly string _connStr;

    public BookingController(IConfiguration config)
    {
        _connStr = config.GetConnectionString("DefaultConnection");
    }

    // Allow specifying quantity and groupMinAge
    public IActionResult Start(int id, int qty = 1, int? groupMinAge = null)
    {
        if (!AuthHelper.IsLoggedIn(HttpContext))
        {
            return RedirectToAction("Login", "Account");
        }

        if (qty < 1) qty = 1;

        int userId = HttpContext.Session.GetInt32("UserId").Value;
        string? userEmail = null;
        int newBookingId = 0;

        using (SqlConnection connection = new SqlConnection(_connStr))
        {
            connection.Open();
            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    // ===== Limit to 3 active future bookings =====
                    var countCmd = new SqlCommand(@"
                        SELECT COUNT(*)
                        FROM Bookings b
                        JOIN Trips t ON b.TripId = t.TripId
                        WHERE b.UserId = @uid
                        AND b.Status = @status",
                        connection, transaction);

                    countCmd.Parameters.AddWithValue("@uid", userId);
                    countCmd.Parameters.AddWithValue("@status", "Active");

                    int activeBookings = (int)countCmd.ExecuteScalar();

                    if (activeBookings >= 3)
                    {
                        transaction.Rollback();
                        TempData["activeBookingsMessage"] = "You cannot book more than 3 upcoming trips.";
                        return RedirectToAction("MyBookings");
                    }

                    // ===== Waiting list priority =====
                    // (moved after locking trip row so we can compare notified slots against available rooms)

                    // ===== Lock trip row and check rooms =====
                    var roomCmd = new SqlCommand(@"
                        SELECT AvailableRooms, MinAge FROM Trips WITH (UPDLOCK)
                        WHERE TripId = @tid",
                        connection, transaction);

                    roomCmd.Parameters.AddWithValue("@tid", id);
                    var roomsReader = roomCmd.ExecuteReader();
                    if (!roomsReader.Read())
                    {
                        roomsReader.Close();
                        transaction.Rollback();
                        return Content("Trip not found.");
                    }

                    int rooms = (int)roomsReader["AvailableRooms"];
                    int? tripMinAge = roomsReader["MinAge"] == DBNull.Value ? null : (int?)roomsReader["MinAge"];
                    roomsReader.Close();

                    // Validate groupMinAge against trip's MinAge (if trip has a limitation)
                    if (tripMinAge.HasValue && groupMinAge.HasValue)
                    {
                        if (groupMinAge.Value < tripMinAge.Value)
                        {
                            transaction.Rollback();
                            TempData["Error"] = $"This trip requires participants to be at least {tripMinAge.Value} years old.";
                            return RedirectToAction("Details", "Trips", new { id = id });
                        }
                    }

                    // If trip requires a minimum age but user didn't provide groupMinAge, block
                    if (tripMinAge.HasValue && !groupMinAge.HasValue)
                    {
                        transaction.Rollback();
                        TempData["Error"] = $"This trip requires participants to be at least {tripMinAge.Value} years old. Please specify the group's minimum age.";
                        return RedirectToAction("Details", "Trips", new { id = id });
                    }

                    if (rooms <= 0 || rooms < qty)
                    {
                        transaction.Rollback();
                        return RedirectToAction("Join", "WaitingList", new { tripId = id });
                    }

                    // Now evaluate waiting list notified slots vs available rooms
                    var activeNotifiedCmd = new SqlCommand(@"
                        SELECT COUNT(*) FROM WaitingList
                        WHERE TripId=@tid AND ExpirationAt IS NOT NULL AND GETDATE() < ExpirationAt", connection, transaction);
                    activeNotifiedCmd.Parameters.AddWithValue("@tid", id);
                    int activeNotified = (int)activeNotifiedCmd.ExecuteScalar();

                    if (activeNotified > 0)
                    {
                        // If there are more or equal notified users than available rooms, only notified users may book.
                        if (rooms <= activeNotified)
                        {
                            var myNotifCmd = new SqlCommand(@"
                                SELECT COUNT(*) FROM WaitingList
                                WHERE TripId=@tid AND UserId=@uid AND ExpirationAt IS NOT NULL AND GETDATE() < ExpirationAt", connection, transaction);
                            myNotifCmd.Parameters.AddWithValue("@tid", id);
                            myNotifCmd.Parameters.AddWithValue("@uid", userId);
                            int isNotified = (int)myNotifCmd.ExecuteScalar();

                            if (isNotified == 0)
                            {
                                transaction.Rollback();
                                return Content("You are not first in the waiting list.");
                            }
                        }
                        // else: there are more rooms than currently-notified users -> allow anyone to book remaining rooms
                    }

                    // ===== Create or update booking (with quantity) =====
                    var existingCmd = new SqlCommand(@"
                        SELECT BookingId, ISNULL(Quantity,1) AS Quantity FROM Bookings WHERE UserId=@uid AND TripId=@tid AND Status='Active'", connection, transaction);
                    existingCmd.Parameters.AddWithValue("@uid", userId);
                    existingCmd.Parameters.AddWithValue("@tid", id);

                    int existingBookingId = 0;
                    int existingQty = 0;

                    using (var exReader = existingCmd.ExecuteReader())
                    {
                        if (exReader.Read())
                        {
                            existingBookingId = (int)exReader["BookingId"];
                            existingQty = exReader["Quantity"] == DBNull.Value ? 1 : Convert.ToInt32(exReader["Quantity"]);
                        }
                    }

                    if (existingBookingId > 0)
                    {
                        // set absolute new quantity to avoid double increments from concurrent executions
                        int newQty = existingQty + qty;
                        var updCmd = new SqlCommand(@"UPDATE Bookings SET Quantity = @newQty, GroupMinAge = @gmin WHERE BookingId=@bid", connection, transaction);
                        updCmd.Parameters.AddWithValue("@newQty", newQty);
                        updCmd.Parameters.AddWithValue("@bid", existingBookingId);
                        updCmd.Parameters.AddWithValue("@gmin", (object)groupMinAge ?? DBNull.Value);
                        updCmd.ExecuteNonQuery();

                        newBookingId = existingBookingId;
                    }
                    else
                    {
                        var bookCmd = new SqlCommand(@"
                        INSERT INTO Bookings (UserId, TripId, Status, Quantity, GroupMinAge)
                        OUTPUT INSERTED.BookingId
                        VALUES (@uid, @tid, @status, @qty, @gmin)",
                        connection, transaction);

                        bookCmd.Parameters.AddWithValue("@uid", userId);
                        bookCmd.Parameters.AddWithValue("@tid", id);
                        bookCmd.Parameters.AddWithValue("@status", "Active");
                        bookCmd.Parameters.AddWithValue("@qty", qty);
                        bookCmd.Parameters.AddWithValue("@gmin", (object)groupMinAge ?? DBNull.Value);
                        newBookingId = (int)bookCmd.ExecuteScalar();
                    }

                    // ===== Update rooms =====
                    var updateCmd = new SqlCommand(@"
                        UPDATE Trips
                        SET AvailableRooms = AvailableRooms - @qty
                        WHERE TripId = @tid",
                        connection, transaction);

                    updateCmd.Parameters.AddWithValue("@tid", id);
                    updateCmd.Parameters.AddWithValue("@qty", qty);
                    updateCmd.ExecuteNonQuery();

                    // ===== Remove from waiting list =====
                    var delCmd = new SqlCommand(@"
                        DELETE FROM WaitingList
                        WHERE TripId = @tid AND UserId = @uid",
                        connection, transaction);

                    delCmd.Parameters.AddWithValue("@tid", id);
                    delCmd.Parameters.AddWithValue("@uid", userId);
                    delCmd.ExecuteNonQuery();

                    // ===== Get user email BEFORE closing =====
                    var emailCmd = new SqlCommand(
                        "SELECT Email FROM Users WHERE UserId = @uid",
                        connection, transaction);

                    emailCmd.Parameters.AddWithValue("@uid", userId);
                    userEmail = emailCmd.ExecuteScalar()?.ToString();
                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    return Content("Booking failed. Please try again.");
                }
            }
        }

        // ===== Send email (outside transaction) =====
        try
        {
            if (!string.IsNullOrEmpty(userEmail))
            {
                EmailHelper.Send(
                    userEmail,
                    "Booking Confirmation",
                    $"Your booking was successful! Trip ID: {id} (Quantity: {qty})"
                );
            }
        }
        catch
        {
            // Email failure should not break booking
        }

        return RedirectToAction("MyBookings");
    }

    // Similar to Start but after booking redirects user directly to payment page
    public IActionResult BookNow(int id, int qty = 1, bool pay = false, int? groupMinAge = null)
    {
        if (!AuthHelper.IsLoggedIn(HttpContext))
        {
            return RedirectToAction("Login", "Account");
        }

        if (qty < 1) qty = 1;

        int userId = HttpContext.Session.GetInt32("UserId").Value;
        string? userEmail = null;
        int newBookingId = 0;

        using (SqlConnection connection = new SqlConnection(_connStr))
        {
            connection.Open();
            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    // limit to 3 active future bookings
                    var countCmd = new SqlCommand(@"
                        SELECT COUNT(*)
                        FROM Bookings b
                        JOIN Trips t ON b.TripId = t.TripId
                        WHERE b.UserId = @uid
                        AND b.Status = @status
                        AND t.StartDate > GETDATE()",
                        connection, transaction);

                    countCmd.Parameters.AddWithValue("@uid", userId);
                    countCmd.Parameters.AddWithValue("@status", "Active");

                    int activeBookings = (int)countCmd.ExecuteScalar();

                    if (activeBookings >= 3)
                    {
                        transaction.Rollback();
                        TempData["activeBookingsMessage"] = "You cannot book more than 3 upcoming trips.";
                        return RedirectToAction("MyBookings");
                    }

                    // lock trip row and check rooms
                    var roomCmd = new SqlCommand(@"
                        SELECT AvailableRooms, MinAge FROM Trips WITH (UPDLOCK)
                        WHERE TripId = @tid",
                        connection, transaction);

                    roomCmd.Parameters.AddWithValue("@tid", id);
                    var rReader = roomCmd.ExecuteReader();
                    if (!rReader.Read())
                    {
                        rReader.Close();
                        transaction.Rollback();
                        return Content("Trip not found.");
                    }

                    int rooms = (int)rReader["AvailableRooms"];
                    int? tripMinAge = rReader["MinAge"] == DBNull.Value ? null : (int?)rReader["MinAge"];
                    rReader.Close();

                    // Validate groupMinAge against trip's MinAge (if trip has a limitation)
                    if (tripMinAge.HasValue && groupMinAge.HasValue)
                    {
                        if (groupMinAge.Value < tripMinAge.Value)
                        {
                            transaction.Rollback();
                            TempData["Error"] = $"This trip requires participants to be at least {tripMinAge.Value} years old.";
                            return RedirectToAction("Details", "Trips", new { id = id });
                        }
                    }

                    // If trip requires a minimum age but user didn't provide groupMinAge, block
                    if (tripMinAge.HasValue && !groupMinAge.HasValue)
                    {
                        transaction.Rollback();
                        TempData["Error"] = $"This trip requires participants to be at least {tripMinAge.Value} years old. Please specify the group's minimum age.";
                        return RedirectToAction("Details", "Trips", new { id = id });
                    }

                    if (rooms <= 0 || rooms < qty)
                    {
                        transaction.Rollback();
                        return RedirectToAction("Join", "WaitingList", new { tripId = id });
                    }

                    // waiting list priority: only restrict booking when notified users occupy all available rooms
                    var activeNotifiedCmd = new SqlCommand(@"
                        SELECT COUNT(*) FROM WaitingList
                        WHERE TripId=@tid AND ExpirationAt IS NOT NULL AND GETDATE() < ExpirationAt", connection, transaction);
                    activeNotifiedCmd.Parameters.AddWithValue("@tid", id);
                    int activeNotified = (int)activeNotifiedCmd.ExecuteScalar();

                    if (activeNotified > 0 && rooms <= activeNotified)
                    {
                        var myNotifCmd = new SqlCommand(@"
                            SELECT COUNT(*) FROM WaitingList
                            WHERE TripId=@tid AND UserId=@uid AND ExpirationAt IS NOT NULL AND GETDATE() < ExpirationAt", connection, transaction);
                        myNotifCmd.Parameters.AddWithValue("@tid", id);
                        myNotifCmd.Parameters.AddWithValue("@uid", userId);
                        int isNotified = (int)myNotifCmd.ExecuteScalar();

                        if (isNotified == 0)
                        {
                            transaction.Rollback();
                            return Content("You are not first in the waiting list.");
                        }
                    }

                    // If user already has an active booking for this trip, increment its Quantity instead of inserting a new row.
                    var existingCmd = new SqlCommand(@"
                        SELECT BookingId, ISNULL(Quantity,1) AS Quantity FROM Bookings WHERE UserId=@uid AND TripId=@tid AND Status='Active'", connection, transaction);
                    existingCmd.Parameters.AddWithValue("@uid", userId);
                    existingCmd.Parameters.AddWithValue("@tid", id);

                    int existingBookingId = 0;
                    int existingQty = 0;

                    using (var exReader = existingCmd.ExecuteReader())
                    {
                        if (exReader.Read())
                        {
                            existingBookingId = (int)exReader["BookingId"];
                            existingQty = exReader["Quantity"] == DBNull.Value ? 1 : Convert.ToInt32(exReader["Quantity"]);
                        }
                    }

                    if (existingBookingId > 0)
                    {
                        int newQty = existingQty + qty;
                        var updCmd = new SqlCommand(@"UPDATE Bookings SET Quantity = @newQty, GroupMinAge = @gmin WHERE BookingId=@bid", connection, transaction);
                        updCmd.Parameters.AddWithValue("@newQty", newQty);
                        updCmd.Parameters.AddWithValue("@bid", existingBookingId);
                        updCmd.Parameters.AddWithValue("@gmin", (object)groupMinAge ?? DBNull.Value);
                        updCmd.ExecuteNonQuery();

                        newBookingId = existingBookingId;
                    }
                    else
                    {
                        var bookCmd = new SqlCommand(@"
                        INSERT INTO Bookings (UserId, TripId, Status, Quantity, GroupMinAge)
                        OUTPUT INSERTED.BookingId
                        VALUES (@uid, @tid, @status, @qty, @gmin)",
                        connection, transaction);

                        bookCmd.Parameters.AddWithValue("@uid", userId);
                        bookCmd.Parameters.AddWithValue("@tid", id);
                        bookCmd.Parameters.AddWithValue("@status", "Active");
                        bookCmd.Parameters.AddWithValue("@qty", qty);
                        bookCmd.Parameters.AddWithValue("@gmin", (object)groupMinAge ?? DBNull.Value);
                        newBookingId = (int)bookCmd.ExecuteScalar();
                    }

                    // update rooms
                    var updateCmd = new SqlCommand(@"
                        UPDATE Trips
                        SET AvailableRooms = AvailableRooms - @qty
                        WHERE TripId = @tid",
                        connection, transaction);

                    updateCmd.Parameters.AddWithValue("@tid", id);
                    updateCmd.Parameters.AddWithValue("@qty", qty);
                    updateCmd.ExecuteNonQuery();

                    // remove from waiting list
                    var delCmd = new SqlCommand(@"
                        DELETE FROM WaitingList
                        WHERE TripId = @tid AND UserId = @uid",
                        connection, transaction);

                    delCmd.Parameters.AddWithValue("@tid", id);
                    delCmd.Parameters.AddWithValue("@uid", userId);
                    delCmd.ExecuteNonQuery();

                    // get user email
                    var emailCmd = new SqlCommand(
                        "SELECT Email FROM Users WHERE UserId = @uid",
                        connection, transaction);

                    emailCmd.Parameters.AddWithValue("@uid", userId);
                    userEmail = emailCmd.ExecuteScalar()?.ToString();
                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    return Content("Booking failed. Please try again.");
                }
            }
        }

        // send email
        try
        {
            if (!string.IsNullOrEmpty(userEmail))
            {
                EmailHelper.Send(userEmail, "Booking Confirmation", $"Your booking was successful! Trip ID: {id} (Quantity: {qty})");
            }
        }
        catch { }

        // If caller requested immediate payment, redirect to Payment page, otherwise go to MyBookings
        if (pay)
        {
            return RedirectToAction("Pay", "Payment", new { bookingId = newBookingId });
        }

        TempData["PaymentMessage"] = "Booking created. Review your bookings below.";
        return RedirectToAction("MyBookings", "Booking");
    }

    //public IActionResult MyBookings()
    //{
    //    if (!AuthHelper.IsLoggedIn(HttpContext))
    //        return RedirectToAction("Login", "Account");

    //    int userId = HttpContext.Session.GetInt32("UserId").Value;
    //    var list = new List<BookingViewModel>();

    //    using (SqlConnection connection = new SqlConnection(_connStr))
    //    {
    //        connection.Open();
    //        var cmd = new SqlCommand(@"
    //            SELECT b.BookingId, t.Destination, t.Country, t.StartDate, b.Status, b.IsPaid, ISNULL(b.Quantity,1) AS Quantity
    //            FROM Bookings b
    //            JOIN Trips t ON b.TripId = t.TripId
    //            WHERE b.UserId = @uid AND b.Status = @status",
    //            connection);

    //        cmd.Parameters.AddWithValue("@uid", userId);
    //        cmd.Parameters.AddWithValue("@status", "Active");

    //        var reader = cmd.ExecuteReader();
    //        while (reader.Read())
    //        {
    //            list.Add(new BookingViewModel
    //            {
    //                BookingId = (int)reader["BookingId"],
    //                Destination = reader["Destination"].ToString(),
    //                Country = reader["Country"].ToString(),
    //                StartDate = (DateTime)reader["StartDate"],
    //                IsPaid = (bool)reader["IsPaid"],
    //                Quantity = reader["Quantity"] == DBNull.Value ? 1 : Convert.ToInt32(reader["Quantity"])
    //            });
    //        }
    //    }

    //    return View(list);
    //}
    public IActionResult MyBookings()
    {
        if (!AuthHelper.IsLoggedIn(HttpContext))
            return RedirectToAction("Login", "Account");

        int userId = HttpContext.Session.GetInt32("UserId").Value;
        var list = new List<BookingViewModel>();

        using (SqlConnection connection = new SqlConnection(_connStr))
        {
            connection.Open();

            var cmd = new SqlCommand(@"
            SELECT
                b.BookingId,
                b.TripId,
                b.BookingDate,
                b.Status,
                b.IsPaid,
                b.PaidAt,
                ISNULL(b.Quantity, 1) AS Quantity,
                b.GroupMinAge,

                t.Destination,
                t.Country,
                t.StartDate,
                t.EndDate,
                t.Price,
                t.Category,
                t.MinAge,
                ISNULL(t.CancellationDays, 0) AS CancellationDays,
                t.ImagePath,
                t.Description
            FROM Bookings b
            JOIN Trips t ON b.TripId = t.TripId
            WHERE b.UserId = @uid AND b.Status = @status
            ORDER BY t.StartDate ASC;", connection);

            cmd.Parameters.AddWithValue("@uid", userId);
            cmd.Parameters.AddWithValue("@status", "Active");

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new BookingViewModel
                {
                    BookingId = (int)reader["BookingId"],
                    TripId = (int)reader["TripId"],
                    BookingDate = (DateTime)reader["BookingDate"],
                    Status = reader["Status"].ToString() ?? "",
                    IsPaid = (bool)reader["IsPaid"],
                    PaidAt = reader["PaidAt"] == DBNull.Value ? null : (DateTime?)reader["PaidAt"],
                    Quantity = Convert.ToInt32(reader["Quantity"]),
                    GroupMinAge = reader["GroupMinAge"] == DBNull.Value ? null : (int?)reader["GroupMinAge"],

                    Destination = reader["Destination"].ToString() ?? "",
                    Country = reader["Country"].ToString() ?? "",
                    StartDate = (DateTime)reader["StartDate"],
                    EndDate = (DateTime)reader["EndDate"],
                    Price = (decimal)reader["Price"],
                    Category = reader["Category"] == DBNull.Value ? null : reader["Category"].ToString(),
                    TripMinAge = reader["MinAge"] == DBNull.Value ? null : (int?)reader["MinAge"],
                    CancellationDays = Convert.ToInt32(reader["CancellationDays"]),
                    ImagePath = reader["ImagePath"] == DBNull.Value ? null : reader["ImagePath"].ToString(),
                    Description = reader["Description"] == DBNull.Value ? null : reader["Description"].ToString(),
                });
            }
        }

        return View(list);
    }


    [HttpPost]
    public IActionResult Cancel(int bookingId)
    {
        if (!AuthHelper.IsLoggedIn(HttpContext))
            return RedirectToAction("Login", "Account");

        int userId = HttpContext.Session.GetInt32("UserId").Value;

        using (SqlConnection connection = new SqlConnection(_connStr))
        {
            connection.Open();
            using var transaction = connection.BeginTransaction();
            try
            {
                var getCmd = new SqlCommand(@"
                    SELECT b.TripId, b.IsPaid, ISNULL(b.Quantity,1) AS Quantity, t.StartDate, ISNULL(t.CancellationDays, 0) AS CancellationDays
                    FROM Bookings b
                    JOIN Trips t ON b.TripId = t.TripId
                    WHERE b.BookingId = @bid
                    AND b.UserId = @uid
                    AND b.Status = @status",
                     connection, transaction);

                getCmd.Parameters.AddWithValue("@bid", bookingId);
                getCmd.Parameters.AddWithValue("@uid", userId);
                getCmd.Parameters.AddWithValue("@status", "Active");

                int tripId;
                bool wasPaid;
                DateTime startDate;
                int cancellationDays;
                int qty;

                using (var reader = getCmd.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        transaction.Rollback();
                        return Content("Booking not found (or already cancelled).");
                    }

                    tripId = (int)reader["TripId"];
                    wasPaid = (bool)reader["IsPaid"];
                    qty = reader["Quantity"] == DBNull.Value ? 1 : Convert.ToInt32(reader["Quantity"]);
                    startDate = (DateTime)reader["StartDate"];
                    cancellationDays = reader["CancellationDays"] == DBNull.Value ? 0 : (int)reader["CancellationDays"];
                }

                // Enforce cancellation day limit
                if (cancellationDays > 0)
                {
                    var daysUntilStart = (startDate - DateTime.Now).TotalDays;
                    if (daysUntilStart < cancellationDays)
                    {
                        transaction.Rollback();
                        TempData["CancellationMessage"] = $"Cancellation not allowed within {cancellationDays} days of the trip start";
                        return RedirectToAction("MyBookings");
                    }
                }

                // Cancel booking
                var cancelCmd = new SqlCommand(@"
                    UPDATE Bookings
                    SET Status = @cancelled
                    WHERE BookingId = @bid
                    AND UserId = @uid",
                    connection, transaction);

                cancelCmd.Parameters.AddWithValue("@cancelled", "Cancelled");
                cancelCmd.Parameters.AddWithValue("@bid", bookingId);
                cancelCmd.Parameters.AddWithValue("@uid", userId);
                cancelCmd.ExecuteNonQuery();

                // Refund if paid
                if (wasPaid)
                {
                    var unpaidCmd = new SqlCommand(@"
                        UPDATE Bookings
                        SET IsPaid = 0, PaidAt = NULL
                        WHERE BookingId = @bid",
                        connection, transaction);
                    unpaidCmd.Parameters.AddWithValue("@bid", bookingId);
                    unpaidCmd.ExecuteNonQuery();

                    var amountCmd = new SqlCommand(@"
                        SELECT t.Price
                        FROM Bookings b
                        JOIN Trips t ON b.TripId = t.TripId
                        WHERE b.BookingId = @bid",
                        connection, transaction);

                    amountCmd.Parameters.AddWithValue("@bid", bookingId);
                    var amountObj = amountCmd.ExecuteScalar();
                    decimal amount = amountObj == null ? 0m : (decimal)amountObj;

                    var refundCmd = new SqlCommand(@"
                        INSERT INTO Payments (BookingId, Amount, Status)
                        VALUES (@bid, @amount, 'Refunded')",
                        connection, transaction);

                    refundCmd.Parameters.AddWithValue("@bid", bookingId);
                    refundCmd.Parameters.AddWithValue("@amount", amount);
                    refundCmd.ExecuteNonQuery();
                }

                // Free up room(s) by qty
                var roomCmd = new SqlCommand(@"
                    UPDATE Trips
                    SET AvailableRooms = AvailableRooms + @qty
                    WHERE TripId = @tid",
                    connection, transaction);

                roomCmd.Parameters.AddWithValue("@tid", tripId);
                roomCmd.Parameters.AddWithValue("@qty", qty);
                roomCmd.ExecuteNonQuery();

                transaction.Commit();

                // ?? Trigger waiting list check (automatic 24h notification)
                WaitingListHelper.ProcessTripWaitingList(_connStr, tripId);

                TempData["PaymentMessage"] = "Booking cancelled successfully.";
                return RedirectToAction("MyBookings");
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return Content(ex.Message);
            }
        }
    }

    public IActionResult PastTrips()
    {
        if (!AuthHelper.IsLoggedIn(HttpContext))
            return RedirectToAction("Login", "Account");

        int userId = HttpContext.Session.GetInt32("UserId").Value;
        var list = new List<BookingViewModel>();

        using (SqlConnection connection = new SqlConnection(_connStr))
        {
            connection.Open();

            var cmd = new SqlCommand(@"
                SELECT b.BookingId, t.Destination, t.Country, t.StartDate
                FROM Bookings b
                JOIN Trips t ON b.TripId = t.TripId
                WHERE b.UserId = @uid
                AND t.StartDate < GETDATE()", connection);

            cmd.Parameters.AddWithValue("@uid", userId);

            var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new BookingViewModel
                {
                    BookingId = (int)reader["BookingId"],
                    Destination = reader["Destination"].ToString(),
                    Country = reader["Country"].ToString(),
                    StartDate = (DateTime)reader["StartDate"]
                });
            }
        }

        return View(list);
    }

    public IActionResult Index()
    {
        return View();
    }
}
