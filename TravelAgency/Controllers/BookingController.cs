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

    public IActionResult Start(int id)
    {
        if (!AuthHelper.IsLoggedIn(HttpContext))
        {
            return RedirectToAction("Login", "Account");
        }

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
                    var waitCmd = new SqlCommand(@"
                        SELECT TOP 1 UserId FROM WaitingList
                        WHERE TripId = @tid
                        ORDER BY JoinDate",
                        connection, transaction);

                    waitCmd.Parameters.AddWithValue("@tid", id);
                    var first = waitCmd.ExecuteScalar();

                    if (first != null && (int)first != userId)
                    {
                        transaction.Rollback();
                        return Content("You are not first in the waiting list.");
                    }

                    // ===== Lock trip row and check rooms =====
                    var roomCmd = new SqlCommand(@"
                        SELECT AvailableRooms FROM Trips WITH (UPDLOCK)
                        WHERE TripId = @tid",
                        connection, transaction);

                    roomCmd.Parameters.AddWithValue("@tid", id);
                    var roomsObj = roomCmd.ExecuteScalar();

                    if (roomsObj == null)
                    {
                        transaction.Rollback();
                        return Content("Trip not found.");
                    }

                    int rooms = (int)roomsObj;

                    if (rooms <= 0)
                    {
                        transaction.Rollback();
                        return RedirectToAction("Join", "WaitingList", new { tripId = id });
                    }

                    // ===== Create booking =====
                    var bookCmd = new SqlCommand(@"
                        INSERT INTO Bookings (UserId, TripId, Status)
                        OUTPUT INSERTED.BookingId
                        VALUES (@uid, @tid, @status)",
                        connection, transaction);

                    bookCmd.Parameters.AddWithValue("@uid", userId);
                    bookCmd.Parameters.AddWithValue("@tid", id);
                    bookCmd.Parameters.AddWithValue("@status", "Active");
                    newBookingId = (int)bookCmd.ExecuteScalar();

                    // ===== Update rooms =====
                    var updateCmd = new SqlCommand(@"
                        UPDATE Trips
                        SET AvailableRooms = AvailableRooms - 1
                        WHERE TripId = @tid",
                        connection, transaction);

                    updateCmd.Parameters.AddWithValue("@tid", id);
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
                    $"Your booking was successful! Trip ID: {id}"
                );
            }
        }
        catch
        {
            // Email failure should not break booking
        }

        // After creating the booking, redirect the user to their bookings page
        // so they can review bookings and proceed with payment from there.
        return RedirectToAction("MyBookings");
    }

    // Similar to Start but after booking redirects user directly to payment page
    public IActionResult BookNow(int id)
    {
        if (!AuthHelper.IsLoggedIn(HttpContext))
        {
            return RedirectToAction("Login", "Account");
        }

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

                    // waiting list priority
                    var waitCmd = new SqlCommand(@"
                        SELECT TOP 1 UserId FROM WaitingList
                        WHERE TripId = @tid
                        ORDER BY JoinDate",
                        connection, transaction);

                    waitCmd.Parameters.AddWithValue("@tid", id);
                    var first = waitCmd.ExecuteScalar();

                    if (first != null && (int)first != userId)
                    {
                        transaction.Rollback();
                        return Content("You are not first in the waiting list.");
                    }

                    // lock trip row and check rooms
                    var roomCmd = new SqlCommand(@"
                        SELECT AvailableRooms FROM Trips WITH (UPDLOCK)
                        WHERE TripId = @tid",
                        connection, transaction);

                    roomCmd.Parameters.AddWithValue("@tid", id);
                    var roomsObj = roomCmd.ExecuteScalar();

                    if (roomsObj == null)
                    {
                        transaction.Rollback();
                        return Content("Trip not found.");
                    }

                    int rooms = (int)roomsObj;

                    if (rooms <= 0)
                    {
                        transaction.Rollback();
                        return RedirectToAction("Join", "WaitingList", new { tripId = id });
                    }

                    // create booking
                    var bookCmd = new SqlCommand(@"
                        INSERT INTO Bookings (UserId, TripId, Status)
                        OUTPUT INSERTED.BookingId
                        VALUES (@uid, @tid, @status)",
                        connection, transaction);

                    bookCmd.Parameters.AddWithValue("@uid", userId);
                    bookCmd.Parameters.AddWithValue("@tid", id);
                    bookCmd.Parameters.AddWithValue("@status", "Active");
                    newBookingId = (int)bookCmd.ExecuteScalar();

                    // update rooms
                    var updateCmd = new SqlCommand(@"
                        UPDATE Trips
                        SET AvailableRooms = AvailableRooms - 1
                        WHERE TripId = @tid",
                        connection, transaction);

                    updateCmd.Parameters.AddWithValue("@tid", id);
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
                EmailHelper.Send(userEmail, "Booking Confirmation", $"Your booking was successful! Trip ID: {id}");
            }
        }
        catch { }

        // redirect to payment for immediate payment
        return RedirectToAction("Pay", "Payment", new { bookingId = newBookingId });
    }

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
                SELECT b.BookingId, t.Destination, t.Country, t.StartDate, b.Status, b.IsPaid
                FROM Bookings b
                JOIN Trips t ON b.TripId = t.TripId
                WHERE b.UserId = @uid AND b.Status = @status",
                connection);

            cmd.Parameters.AddWithValue("@uid", userId);
            cmd.Parameters.AddWithValue("@status", "Active");

            var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new BookingViewModel
                {
                    BookingId = (int)reader["BookingId"],
                    Destination = reader["Destination"].ToString(),
                    Country = reader["Country"].ToString(),
                    StartDate = (DateTime)reader["StartDate"],
                    IsPaid = (bool)reader["IsPaid"]
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
                    SELECT TripId, IsPaid FROM Bookings
                    WHERE BookingId = @bid
                    AND UserId = @uid
                    AND Status = @status",
                    connection, transaction);

                getCmd.Parameters.AddWithValue("@bid", bookingId);
                getCmd.Parameters.AddWithValue("@uid", userId);
                getCmd.Parameters.AddWithValue("@status", "Active");

                int tripId;
                bool wasPaid;

                using (var reader = getCmd.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        transaction.Rollback();
                        return Content("Booking not found (or already cancelled).");
                    }

                    tripId = (int)reader["TripId"];
                    wasPaid = (bool)reader["IsPaid"];
                }
                // Get destination for email
                var destCmd = new SqlCommand(@"
                    SELECT Destination
                    FROM Trips
                    WHERE TripId = @tid",
                    connection, transaction);

                destCmd.Parameters.AddWithValue("@tid", tripId);
                string destination = destCmd.ExecuteScalar()?.ToString() ?? "your trip";


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
                
                if (wasPaid)
                {
                    // 1) Mark booking unpaid
                    var unpaidCmd = new SqlCommand(@"
                        UPDATE Bookings
                        SET IsPaid = 0, PaidAt = NULL
                        WHERE BookingId = @bid",
                        connection, transaction);

                    unpaidCmd.Parameters.AddWithValue("@bid", bookingId);
                    unpaidCmd.ExecuteNonQuery();

                    // 2) Insert a refund record into Payments (keep history)
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
                    VALUES (@bid, @amount, @status)",
                        connection, transaction);

                    refundCmd.Parameters.AddWithValue("@bid", bookingId);
                    refundCmd.Parameters.AddWithValue("@amount", amount);
                    refundCmd.Parameters.AddWithValue("@status", "Refunded");
                    refundCmd.ExecuteNonQuery();

                    // 3) Email the user about refund (simulated)
                    try
                    {
                        var userEmailCmd = new SqlCommand(@"
                        SELECT u.Email
                        FROM Users u
                        JOIN Bookings b ON u.UserId = b.UserId
                        WHERE b.BookingId = @bid",
                            connection, transaction);

                        userEmailCmd.Parameters.AddWithValue("@bid", bookingId);
                        var userEmail = userEmailCmd.ExecuteScalar()?.ToString();

                        if (!string.IsNullOrEmpty(userEmail))
                        {
                            EmailHelper.Send(
                                userEmail,
                                "Booking Cancellation",
                                $"Your booking to {destination} has been cancelled. Since you already paid, a refund will be issued to your original payment method."
                            );
                        }
                    }
                    catch { }
                }
                
                
                
                
                var roomCmd = new SqlCommand(@"
                    UPDATE Trips
                    SET AvailableRooms = AvailableRooms + 1
                    WHERE TripId = @tid",
                    connection, transaction);

                roomCmd.Parameters.AddWithValue("@tid", tripId);
                roomCmd.ExecuteNonQuery();

                // ===== Notify first in waiting list =====
                try
                {
                    var waitCmd = new SqlCommand(@"
                        SELECT TOP 1 u.Email
                        FROM WaitingList w
                        JOIN Users u ON w.UserId = u.UserId
                        WHERE w.TripId = @tid
                        ORDER BY w.JoinDate",
                        connection, transaction);

                    waitCmd.Parameters.AddWithValue("@tid", tripId);
                    var email = waitCmd.ExecuteScalar()?.ToString();

                    if (!string.IsNullOrEmpty(email))
                    {
                        EmailHelper.Send(
                            email,
                            "Seat Available Notification",
                            "Good news! A seat is now available for a trip you are waiting for. Please log in and complete your booking."
                        );
                    }
                }
                catch { }

                transaction.Commit();
                
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
