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
                    var alreadyBookedCmd = new SqlCommand(@"
                        SELECT COUNT(*)
                        FROM Bookings
                        WHERE UserId=@uid AND TripId=@tid AND Status='Active'", connection, transaction);
                    alreadyBookedCmd.Parameters.AddWithValue("@uid", userId);
                    alreadyBookedCmd.Parameters.AddWithValue("@tid", id);
                    bool alreadyBooked = (int)alreadyBookedCmd.ExecuteScalar() > 0;

                    var countCmd = new SqlCommand(@"
                        SELECT COUNT(DISTINCT b.TripId)
                        FROM Bookings b
                        JOIN Trips t ON b.TripId = t.TripId
                        WHERE b.UserId = @uid
                          AND b.Status = 'Active'
                          AND t.StartDate > GETDATE()
                          AND b.TripId <> @tid", connection, transaction);

                    countCmd.Parameters.AddWithValue("@uid", userId);
                    countCmd.Parameters.AddWithValue("@tid", id);

                    int otherUpcomingTrips = (int)countCmd.ExecuteScalar();

                    int distinctAfter = otherUpcomingTrips + (alreadyBooked ? 0 : 1);

                    if (!alreadyBooked && distinctAfter >= 4)
                    {
                        transaction.Rollback();
                        TempData["activeBookingsMessage"] = "You cannot book more than 3 upcoming trips.";
                        return RedirectToAction("MyBookings");
                    }

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

                    if (tripMinAge.HasValue && groupMinAge.HasValue)
                    {
                        if (groupMinAge.Value < tripMinAge.Value)
                        {
                            transaction.Rollback();
                            TempData["Error"] = $"This trip requires participants to be at least {tripMinAge.Value} years old.";
                            return RedirectToAction("Details", "Trips", new { id = id });
                        }
                    }

                    if (tripMinAge.HasValue && !groupMinAge.HasValue)
                    {
                        transaction.Rollback();
                        TempData["Error"] = $"This trip requires participants to be at least {tripMinAge.Value} years old. Please specify the group's minimum age.";
                        return RedirectToAction("Details", "Trips", new { id = id });
                    }

                    if (rooms <= 0 || rooms < qty)
                    {
                        transaction.Rollback();
                        return RedirectToAction("Status", "WaitingList", new { tripId = id });
                    }

                    var activeNotifiedCmd = new SqlCommand(@"
                        SELECT COUNT(*) FROM WaitingList
                        WHERE TripId=@tid AND ExpirationAt IS NOT NULL AND GETDATE() < ExpirationAt", connection, transaction);
                    activeNotifiedCmd.Parameters.AddWithValue("@tid", id);
                    int activeNotified = (int)activeNotifiedCmd.ExecuteScalar();

                    if (activeNotified > 0)
                    {
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
                    }

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

                    var updateCmd = new SqlCommand(@"
                        UPDATE Trips
                        SET AvailableRooms = AvailableRooms - @qty
                        WHERE TripId = @tid",
                        connection, transaction);

                    updateCmd.Parameters.AddWithValue("@tid", id);
                    updateCmd.Parameters.AddWithValue("@qty", qty);
                    updateCmd.ExecuteNonQuery();

                    var delCmd = new SqlCommand(@"
                        DELETE FROM WaitingList
                        WHERE TripId = @tid AND UserId = @uid",
                        connection, transaction);

                    delCmd.Parameters.AddWithValue("@tid", id);
                    delCmd.Parameters.AddWithValue("@uid", userId);
                    delCmd.ExecuteNonQuery();

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

        try
        {
            if (!string.IsNullOrEmpty(userEmail))
            {
                string title = "";
                string destination = "";
                string country = "";
                DateTime? startDate = null;
                DateTime? endDate = null;

                using (var conn = new SqlConnection(_connStr))
                {
                    conn.Open();
                    var infoCmd = new SqlCommand(@"
                        SELECT PackageName, Destination, Country, StartDate, EndDate
                        FROM Trips
                        WHERE TripId = @tid", conn);
                    infoCmd.Parameters.AddWithValue("@tid", id);
                    using var r = infoCmd.ExecuteReader();
                    if (r.Read())
                    {
                        title = (r["PackageName"] == DBNull.Value ? "" : r["PackageName"]?.ToString() ?? "").Trim();
                        destination = r["Destination"]?.ToString() ?? "";
                        country = r["Country"]?.ToString() ?? "";
                        startDate = r["StartDate"] == DBNull.Value ? null : (DateTime?)r["StartDate"];
                        endDate = r["EndDate"] == DBNull.Value ? null : (DateTime?)r["EndDate"];
                    }
                }

                var displayTitle = !string.IsNullOrWhiteSpace(title) ? title : $"{destination}, {country}";
                var dateRange = (startDate.HasValue && endDate.HasValue)
                    ? $"{startDate.Value:dd/MM/yyyy} - {endDate.Value:dd/MM/yyyy}"
                    : "(dates unavailable)";

                var body =
                    $"Thank you for your booking!\n\n" +
                    $"Trip: {displayTitle}\n" +
                    $"Dates: {dateRange}\n" +
                    $"Rooms: {qty}\n\n" +
                    $"You can manage your booking and download your itinerary anytime from: /Booking/MyBookings\n\n" +
                    $"Travel Agency";

                EmailHelper.Send(userEmail, "Booking Confirmation", body);
            }
        }
        catch
        {
        }

        return RedirectToAction("MyBookings");
    }

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
                    var alreadyBookedCmd = new SqlCommand(@"
                        SELECT COUNT(*)
                        FROM Bookings
                        WHERE UserId=@uid AND TripId=@tid AND Status='Active'", connection, transaction);
                    alreadyBookedCmd.Parameters.AddWithValue("@uid", userId);
                    alreadyBookedCmd.Parameters.AddWithValue("@tid", id);
                    bool alreadyBooked = (int)alreadyBookedCmd.ExecuteScalar() > 0;

                    var countCmd = new SqlCommand(@"
                        SELECT COUNT(DISTINCT b.TripId)
                        FROM Bookings b
                        JOIN Trips t ON b.TripId = t.TripId
                        WHERE b.UserId = @uid
                          AND b.Status = 'Active'
                          AND t.StartDate > GETDATE()
                          AND b.TripId <> @tid", connection, transaction);

                    countCmd.Parameters.AddWithValue("@uid", userId);
                    countCmd.Parameters.AddWithValue("@tid", id);

                    int otherUpcomingTrips = (int)countCmd.ExecuteScalar();
                    int distinctAfter = otherUpcomingTrips + (alreadyBooked ? 0 : 1);

                    if (!alreadyBooked && distinctAfter >= 4)
                    {
                        transaction.Rollback();
                        TempData["activeBookingsMessage"] = "You cannot book more than 3 upcoming trips.";
                        return RedirectToAction("MyBookings");
                    }

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

                    if (tripMinAge.HasValue && groupMinAge.HasValue)
                    {
                        if (groupMinAge.Value < tripMinAge.Value)
                        {
                            transaction.Rollback();
                            TempData["Error"] = $"This trip requires participants to be at least {tripMinAge.Value} years old.";
                            return RedirectToAction("Details", "Trips", new { id = id });
                        }
                    }

                    if (tripMinAge.HasValue && !groupMinAge.HasValue)
                    {
                        transaction.Rollback();
                        TempData["Error"] = $"This trip requires participants to be at least {tripMinAge.Value} years old. Please specify the group's minimum age.";
                        return RedirectToAction("Details", "Trips", new { id = id });
                    }

                    if (rooms <= 0 || rooms < qty)
                    {
                        transaction.Rollback();
                        return RedirectToAction("Status", "WaitingList", new { tripId = id });
                    }

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

                    var updateCmd = new SqlCommand(@"
                        UPDATE Trips
                        SET AvailableRooms = AvailableRooms - @qty
                        WHERE TripId = @tid",
                        connection, transaction);

                    updateCmd.Parameters.AddWithValue("@tid", id);
                    updateCmd.Parameters.AddWithValue("@qty", qty);
                    updateCmd.ExecuteNonQuery();

                    var delCmd = new SqlCommand(@"
                        DELETE FROM WaitingList
                        WHERE TripId = @tid AND UserId = @uid",
                        connection, transaction);

                    delCmd.Parameters.AddWithValue("@tid", id);
                    delCmd.Parameters.AddWithValue("@uid", userId);
                    delCmd.ExecuteNonQuery();

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

        try
        {
            if (!string.IsNullOrEmpty(userEmail))
            {
                string title = "";
                string destination = "";
                string country = "";
                DateTime? startDate = null;
                DateTime? endDate = null;

                using (var conn = new SqlConnection(_connStr))
                {
                    conn.Open();
                    var infoCmd = new SqlCommand(@"
                        SELECT PackageName, Destination, Country, StartDate, EndDate
                        FROM Trips
                        WHERE TripId = @tid", conn);
                    infoCmd.Parameters.AddWithValue("@tid", id);
                    using var r = infoCmd.ExecuteReader();
                    if (r.Read())
                    {
                        title = (r["PackageName"] == DBNull.Value ? "" : r["PackageName"]?.ToString() ?? "").Trim();
                        destination = r["Destination"]?.ToString() ?? "";
                        country = r["Country"]?.ToString() ?? "";
                        startDate = r["StartDate"] == DBNull.Value ? null : (DateTime?)r["StartDate"];
                        endDate = r["EndDate"] == DBNull.Value ? null : (DateTime?)r["EndDate"];
                    }
                }

                var displayTitle = !string.IsNullOrWhiteSpace(title) ? title : $"{destination}, {country}";
                var dateRange = (startDate.HasValue && endDate.HasValue)
                    ? $"{startDate.Value:dd/MM/yyyy} - {endDate.Value:dd/MM/yyyy}"
                    : "(dates unavailable)";

                var body =
                    $"Thank you for your booking!\n\n" +
                    $"Trip: {displayTitle}\n" +
                    $"Dates: {dateRange}\n" +
                    $"Rooms: {qty}\n\n" +
                    $"If you chose to pay now, please complete your payment.\n" +
                    $"You can manage your booking and download your itinerary anytime from: /Booking/MyBookings\n\n" +
                    $"Travel Agency";

                EmailHelper.Send(userEmail, "Booking Confirmation", body);
            }
        }
        catch { }

        if (pay)
        {
            return RedirectToAction("Pay", "Payment", new { bookingId = newBookingId });
        }

        TempData["PaymentMessage"] = "Booking created. Review your bookings below.";
        return RedirectToAction("MyBookings", "Booking");
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
            SELECT
                b.BookingId,
                b.TripId,
                b.BookingDate,
                b.Status,
                b.IsPaid,
                b.PaidAt,
                ISNULL(b.Quantity, 1) AS Quantity,
                b.GroupMinAge,

                t.PackageName,
                t.Destination,
                t.Country,
                t.StartDate,
                t.EndDate,
                t.Price,
                t.Category,
                t.MinAge,
                ISNULL(t.CancellationDays, 0) AS CancellationDays,
                t.AvailableRooms,
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

                    PackageName = reader["PackageName"] == DBNull.Value ? "" : reader["PackageName"].ToString() ?? "",
                    Destination = reader["Destination"].ToString() ?? "",
                    Country = reader["Country"].ToString() ?? "",
                    StartDate = (DateTime)reader["StartDate"],
                    EndDate = (DateTime)reader["EndDate"],
                    Price = (decimal)reader["Price"],
                    Category = reader["Category"] == DBNull.Value ? null : reader["Category"].ToString(),
                    TripMinAge = reader["MinAge"] == DBNull.Value ? null : (int?)reader["MinAge"],
                    CancellationDays = Convert.ToInt32(reader["CancellationDays"]),
                    AvailableRooms = Convert.ToInt32(reader["AvailableRooms"]),
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

        string? userEmail = null;
        string packageName = "";
        string destination = "";
        string country = "";
        DateTime? tripStartDateForEmail = null;
        DateTime? tripEndDateForEmail = null;

        using (SqlConnection connection = new SqlConnection(_connStr))
        {
            connection.Open();
            using var transaction = connection.BeginTransaction();
            try
            {
                var getCmd = new SqlCommand(@"
                    SELECT
                        b.TripId,
                        b.IsPaid,
                        ISNULL(b.Quantity,1) AS Quantity,
                        t.StartDate,
                        t.EndDate,
                        ISNULL(t.CancellationDays, 0) AS CancellationDays,
                        t.PackageName,
                        t.Destination,
                        t.Country,
                        u.Email
                    FROM Bookings b
                    JOIN Trips t ON b.TripId = t.TripId
                    JOIN Users u ON b.UserId = u.UserId
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

                    userEmail = reader["Email"] == DBNull.Value ? null : reader["Email"]?.ToString();
                    packageName = reader["PackageName"] == DBNull.Value ? "" : reader["PackageName"]?.ToString() ?? "";
                    destination = reader["Destination"] == DBNull.Value ? "" : reader["Destination"]?.ToString() ?? "";
                    country = reader["Country"] == DBNull.Value ? "" : reader["Country"]?.ToString() ?? "";
                    tripStartDateForEmail = reader["StartDate"] == DBNull.Value ? null : (DateTime?)reader["StartDate"];
                    tripEndDateForEmail = reader["EndDate"] == DBNull.Value ? null : (DateTime?)reader["EndDate"];
                }

                if (cancellationDays > 0)
                {
                    var daysUntilStart = (startDate - DateTime.Now).TotalMinutes;
                    if (daysUntilStart < cancellationDays * 24 * 60)
                    {
                        transaction.Rollback();
                        TempData["CancellationMessage"] = $"Cancellation not allowed within {cancellationDays} days of the trip start";
                        return RedirectToAction("MyBookings");
                    }
                }

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

                var roomCmd = new SqlCommand(@"
                    UPDATE Trips
                    SET AvailableRooms = AvailableRooms + @qty
                    WHERE TripId = @tid",
                    connection, transaction);

                roomCmd.Parameters.AddWithValue("@tid", tripId);
                roomCmd.Parameters.AddWithValue("@qty", qty);
                roomCmd.ExecuteNonQuery();

                transaction.Commit();

                WaitingListHelper.ProcessTripWaitingList(_connStr, tripId);

                try
                {
                    if (!string.IsNullOrWhiteSpace(userEmail))
                    {
                        var title = (packageName ?? "").Trim();
                        var displayTitle = !string.IsNullOrWhiteSpace(title)
                            ? title
                            : $"{destination}, {country}".Trim().Trim(',');

                        var dateRange = (tripStartDateForEmail.HasValue && tripEndDateForEmail.HasValue)
                            ? $"{tripStartDateForEmail.Value:dd/MM/yyyy} - {tripEndDateForEmail.Value:dd/MM/yyyy}"
                            : "(dates unavailable)";

                        var body =
                            $"Your booking has been cancelled.\n\n" +
                            $"Trip: {displayTitle}\n" +
                            $"Dates: {dateRange}\n\n" +
                            $"You can view your bookings anytime from: /Booking/MyBookings\n\n" +
                            $"Travel Agency";

                        EmailHelper.Send(userEmail, "Booking Cancelled", body);
                    }
                }
                catch
                {
                }

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
                SELECT
                    b.BookingId,
                    b.TripId,
                    b.Status,
                    t.PackageName,
                    t.Destination,
                    t.Country,
                    t.StartDate,
                    t.EndDate,
                    t.ImagePath
                FROM Bookings b
                JOIN Trips t ON b.TripId = t.TripId
                WHERE b.UserId = @uid
                  AND (b.Status IS NULL OR b.Status <> 'Cancelled')
                  AND (
                        (t.EndDate IS NOT NULL AND t.EndDate < GETDATE())
                        OR (t.EndDate IS NULL AND t.StartDate < GETDATE())
                      )
                ORDER BY t.StartDate DESC;", connection);

            cmd.Parameters.AddWithValue("@uid", userId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new BookingViewModel
                {
                    BookingId = (int)reader["BookingId"],
                    TripId = (int)reader["TripId"],
                    Status = reader["Status"]?.ToString() ?? "",
                    PackageName = reader["PackageName"] == DBNull.Value ? "" : reader["PackageName"]?.ToString() ?? "",
                    Destination = reader["Destination"]?.ToString() ?? "",
                    Country = reader["Country"]?.ToString() ?? "",
                    StartDate = (DateTime)reader["StartDate"],
                    EndDate = reader["EndDate"] == DBNull.Value ? DateTime.MinValue : (DateTime)reader["EndDate"],
                    ImagePath = reader["ImagePath"] == DBNull.Value ? null : reader["ImagePath"]?.ToString(),
                });
            }
        }

        return View(list);
    }

    public IActionResult Index()
    {
        return View();
    }

    public IActionResult ItineraryPdf(int bookingId)
    {
        if (!AuthHelper.IsLoggedIn(HttpContext))
            return RedirectToAction("Login", "Account");

        int userId = HttpContext.Session.GetInt32("UserId").Value;

        BookingViewModel? booking = null;

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

                    t.PackageName,
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
                WHERE b.BookingId = @bid
                  AND b.UserId = @uid
                  AND b.Status = 'Active';", connection);

            cmd.Parameters.AddWithValue("@bid", bookingId);
            cmd.Parameters.AddWithValue("@uid", userId);

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                booking = new BookingViewModel
                {
                    BookingId = (int)reader["BookingId"],
                    TripId = (int)reader["TripId"],
                    BookingDate = (DateTime)reader["BookingDate"],
                    Status = reader["Status"]?.ToString() ?? "",
                    IsPaid = (bool)reader["IsPaid"],
                    PaidAt = reader["PaidAt"] == DBNull.Value ? null : (DateTime?)reader["PaidAt"],
                    Quantity = Convert.ToInt32(reader["Quantity"]),
                    GroupMinAge = reader["GroupMinAge"] == DBNull.Value ? null : (int?)reader["GroupMinAge"],

                    PackageName = reader["PackageName"] == DBNull.Value ? "" : reader["PackageName"].ToString() ?? "",
                    Destination = reader["Destination"]?.ToString() ?? "",
                    Country = reader["Country"]?.ToString() ?? "",
                    StartDate = (DateTime)reader["StartDate"],
                    EndDate = (DateTime)reader["EndDate"],
                    Price = (decimal)reader["Price"],
                    Category = reader["Category"] == DBNull.Value ? null : reader["Category"].ToString(),
                    TripMinAge = reader["MinAge"] == DBNull.Value ? null : (int?)reader["MinAge"],
                    CancellationDays = Convert.ToInt32(reader["CancellationDays"]),
                    ImagePath = reader["ImagePath"] == DBNull.Value ? null : reader["ImagePath"].ToString(),
                    Description = reader["Description"] == DBNull.Value ? null : reader["Description"].ToString(),
                };
            }
        }

        if (booking == null)
            return NotFound();

        if (booking.StartDate.Date < DateTime.Now.Date)
        {
            TempData["Error"] = "You can only download an itinerary for upcoming trips.";
            return RedirectToAction("MyBookings");
        }

        var pdfBytes = ItineraryPdfGenerator.Generate(booking);
        var fileName = $"Itinerary_Booking_{booking.BookingId}_Trip_{booking.TripId}.pdf";
        return File(pdfBytes, "application/pdf", fileName);
    }

    [HttpPost]
    public IActionResult UpdateQuantity(int bookingId, int newQty)
    {
        if (!AuthHelper.IsLoggedIn(HttpContext))
            return RedirectToAction("Login", "Account");

        if (newQty < 1) newQty = 1;

        int userId = HttpContext.Session.GetInt32("UserId")!.Value;

        using var conn = new SqlConnection(_connStr);
        conn.Open();

        using var tx = conn.BeginTransaction();
        try
        {
            var cmd = new SqlCommand(@"
                SELECT
                    b.BookingId,
                    b.UserId,
                    b.TripId,
                    b.Status,
                    b.IsPaid,
                    ISNULL(b.Quantity, 1) AS Quantity,
                    t.AvailableRooms,
                    t.StartDate
                FROM Bookings b
                JOIN Trips t WITH (UPDLOCK) ON b.TripId = t.TripId
                WHERE b.BookingId = @bid
                  AND b.UserId = @uid
                  AND b.Status = 'Active';", conn, tx);

            cmd.Parameters.AddWithValue("@bid", bookingId);
            cmd.Parameters.AddWithValue("@uid", userId);

            int tripId;
            int currentQty;
            int availableRooms;
            DateTime startDate;
            bool isPaid;

            using (var r = cmd.ExecuteReader())
            {
                if (!r.Read())
                {
                    tx.Rollback();
                    TempData["Error"] = "Booking not found.";
                    return RedirectToAction("MyBookings");
                }

                tripId = (int)r["TripId"];
                currentQty = Convert.ToInt32(r["Quantity"]);
                availableRooms = Convert.ToInt32(r["AvailableRooms"]);
                startDate = (DateTime)r["StartDate"];
                isPaid = (bool)r["IsPaid"];
            }

            if (isPaid)
            {
                tx.Rollback();
                TempData["Error"] = "You cannot change room quantity after payment.";
                return RedirectToAction("MyBookings");
            }

            if (newQty == currentQty)
            {
                tx.Rollback();
                return RedirectToAction("MyBookings");
            }

            if (startDate.Date <= DateTime.Now.Date)
            {
                tx.Rollback();
                TempData["Error"] = "You can only change room quantity before the trip starts.";
                return RedirectToAction("MyBookings");
            }

            var activeNotifiedCmd = new SqlCommand(@"
                SELECT COUNT(*) FROM WaitingList
                WHERE TripId=@tid AND ExpirationAt IS NOT NULL AND GETDATE() < ExpirationAt", conn, tx);
            activeNotifiedCmd.Parameters.AddWithValue("@tid", tripId);
            int activeNotified = (int)activeNotifiedCmd.ExecuteScalar();

            if (activeNotified > 0 && availableRooms <= activeNotified)
            {
                var myNotifCmd = new SqlCommand(@"
                    SELECT COUNT(*) FROM WaitingList
                    WHERE TripId=@tid AND UserId=@uid AND ExpirationAt IS NOT NULL AND GETDATE() < ExpirationAt", conn, tx);
                myNotifCmd.Parameters.AddWithValue("@tid", tripId);
                myNotifCmd.Parameters.AddWithValue("@uid", userId);
                int isNotified = (int)myNotifCmd.ExecuteScalar();

                if (isNotified == 0)
                {
                    tx.Rollback();
                    TempData["Error"] = "Waiting list priority is active. You cannot increase rooms right now.";
                    return RedirectToAction("MyBookings");
                }
            }

            int delta = newQty - currentQty;

            if (delta > 0)
            {
                if (availableRooms < delta)
                {
                    tx.Rollback();
                    TempData["Error"] = "Not enough available rooms to increase your booking.";
                    return RedirectToAction("MyBookings");
                }

                var updBooking = new SqlCommand(@"UPDATE Bookings SET Quantity=@q WHERE BookingId=@bid AND UserId=@uid AND Status='Active'", conn, tx);
                updBooking.Parameters.AddWithValue("@q", newQty);
                updBooking.Parameters.AddWithValue("@bid", bookingId);
                updBooking.Parameters.AddWithValue("@uid", userId);
                updBooking.ExecuteNonQuery();

                var updTrip = new SqlCommand(@"UPDATE Trips SET AvailableRooms = AvailableRooms - @d WHERE TripId=@tid", conn, tx);
                updTrip.Parameters.AddWithValue("@d", delta);
                updTrip.Parameters.AddWithValue("@tid", tripId);
                updTrip.ExecuteNonQuery();
            }
            else
            {
                int release = -delta;

                var updBooking = new SqlCommand(@"UPDATE Bookings SET Quantity=@q WHERE BookingId=@bid AND UserId=@uid AND Status='Active'", conn, tx);
                updBooking.Parameters.AddWithValue("@q", newQty);
                updBooking.Parameters.AddWithValue("@bid", bookingId);
                updBooking.Parameters.AddWithValue("@uid", userId);
                updBooking.ExecuteNonQuery();

                var updTrip = new SqlCommand(@"UPDATE Trips SET AvailableRooms = AvailableRooms + @d WHERE TripId=@tid", conn, tx);
                updTrip.Parameters.AddWithValue("@d", release);
                updTrip.Parameters.AddWithValue("@tid", tripId);
                updTrip.ExecuteNonQuery();

            }

            tx.Commit();
        }
        catch
        {
            try { tx.Rollback(); } catch { }
            TempData["Error"] = "Could not update booking quantity. Please try again.";
            return RedirectToAction("MyBookings");
        }

        try
        {
            using var conn2 = new SqlConnection(_connStr);
            conn2.Open();
            var tcmd = new SqlCommand("SELECT TripId FROM Bookings WHERE BookingId=@bid AND UserId=@uid", conn2);
            tcmd.Parameters.AddWithValue("@bid", bookingId);
            tcmd.Parameters.AddWithValue("@uid", userId);
            var tidObj = tcmd.ExecuteScalar();
            if (tidObj != null && tidObj != DBNull.Value)
            {
                WaitingListHelper.ProcessTripWaitingList(_connStr, Convert.ToInt32(tidObj));
            }
        }
        catch { }

        TempData["PaymentMessage"] = "Booking updated.";
        return RedirectToAction("MyBookings");
    }
}
