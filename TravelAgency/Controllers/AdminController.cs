using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using TravelAgency.Models;
using TravelAgency.Helpers;

// ? added (does not remove anything)
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TravelAgency.Controllers
{
    public class AdminController : Controller
    {
        private readonly string _connStr;

        public AdminController(IConfiguration configuration)
        {
            _connStr = configuration.GetConnectionString("DefaultConnection");
        }

        public IActionResult Trips()
        {
            if (!AuthHelper.IsAdmin(HttpContext))
                return RedirectToAction("Login", "Account");

            var trips = new List<Trip>();
            using (var conn = new SqlConnection(_connStr))
            {
                conn.Open();
                var cmd = new SqlCommand("SELECT * FROM Trips", conn);
                var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    trips.Add(new Trip
                    {
                        TripId = (int)reader["TripId"],
                        Destination = reader["Destination"].ToString(),
                        Country = reader["Country"].ToString(),
                        Price = (decimal)reader["Price"],
                        AvailableRooms = (int)reader["AvailableRooms"]
                    });
                }
                conn.Close();
            }
            return View(trips);
        }

        [HttpGet]
        [HttpGet]
        public IActionResult AddTrip()
        {
            if (!AuthHelper.IsAdmin(HttpContext))
                return RedirectToAction("Login", "Account");

            var t = new Trip();
            t.StartDate = DateTime.Today;
            t.EndDate = DateTime.Today.AddDays(1); // או 3 ימים, מה שאתה רוצה
            return View(t);
        }


        // - main image: imageFile (same name you already use)
        // - additional images: galleryImages (multiple)
        [HttpPost]
        public IActionResult AddTrip(Trip trip, IFormFile? imageFile, List<IFormFile>? galleryImages)
        {
            if (!AuthHelper.IsAdmin(HttpContext))
                return RedirectToAction("Login", "Account");

            if (!ModelState.IsValid)
            {
                return Content(string.Join(" | ",
                    ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)));
            }

            // SQL Server datetime valid range starts at 1753-01-01
            var sqlMin = new DateTime(1753, 1, 1);

            if (trip.StartDate == default(DateTime) || trip.EndDate == default(DateTime) ||
                trip.StartDate < sqlMin || trip.EndDate < sqlMin)
            {
                TempData["Error"] = "יש לבחור Start Date ו-End Date תקינים.";
                return View(trip);
            }

            if (trip.EndDate <= trip.StartDate)
            {
                TempData["Error"] = "End date must be after start date";
                return View(trip);
            }

            // Ensure folder exists
            var imagesPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/images/trips");
            if (!Directory.Exists(imagesPath))
                Directory.CreateDirectory(imagesPath);

            // Save MAIN image (existing behavior)
            if (imageFile != null && imageFile.Length > 0)
            {
                var fileName = Guid.NewGuid() + Path.GetExtension(imageFile.FileName);
                var fullPath = Path.Combine(imagesPath, fileName);
                using var stream = new FileStream(fullPath, FileMode.Create);
                imageFile.CopyTo(stream);

                trip.ImagePath = "/images/trips/" + fileName;
            }

            int newTripId;

            using (var conn = new SqlConnection(_connStr))
            {
                conn.Open();

                var cmd = new SqlCommand(@"
                    INSERT INTO Trips
                    (Destination, Country, StartDate, EndDate, Price, AvailableRooms, Category, MinAge, Description, ImagePath)
                    OUTPUT INSERTED.TripId
                    VALUES
                    (@Destination, @Country, @StartDate, @EndDate, @Price, @Rooms, @Category, @MinAge, @Description, @ImagePath)", conn);

                cmd.Parameters.AddWithValue("@Destination", trip.Destination);
                cmd.Parameters.AddWithValue("@Country", trip.Country);
                cmd.Parameters.AddWithValue("@StartDate", trip.StartDate);
                cmd.Parameters.AddWithValue("@EndDate", trip.EndDate);
                cmd.Parameters.AddWithValue("@Price", trip.Price);
                cmd.Parameters.AddWithValue("@Rooms", trip.AvailableRooms);

                cmd.Parameters.AddWithValue("@Category", (object?)trip.Category ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@MinAge", (object?)trip.MinAge ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Description", (object?)trip.Description ?? DBNull.Value);

                cmd.Parameters.AddWithValue("@ImagePath", (object?)trip.ImagePath ?? DBNull.Value);

                newTripId = (int)cmd.ExecuteScalar();

                if (galleryImages != null && galleryImages.Count > 0)
                {
                    foreach (var img in galleryImages)
                    {
                        if (img == null || img.Length == 0) continue;

                        var fileName = Guid.NewGuid() + Path.GetExtension(img.FileName);
                        var fullPath = Path.Combine(imagesPath, fileName);

                        using (var stream = new FileStream(fullPath, FileMode.Create))
                        {
                            img.CopyTo(stream);
                        }

                        var dbPath = "/images/trips/" + fileName;

                        var imgCmd = new SqlCommand(@"
                            INSERT INTO TripImages (TripId, ImagePath)
                            VALUES (@tid, @path)", conn);

                        imgCmd.Parameters.AddWithValue("@tid", newTripId);
                        imgCmd.Parameters.AddWithValue("@path", dbPath);
                        imgCmd.ExecuteNonQuery();
                    }
                }

                conn.Close();
            }

            TempData["Success"] = "Trip added successfully.";
            return RedirectToAction("Trips");
        }

        [HttpGet]
        public IActionResult EditTrip(int id)
        {
            if (!AuthHelper.IsAdmin(HttpContext))
                return RedirectToAction("Login", "Account");

            Trip trip = null;
            using (var conn = new SqlConnection(_connStr))
            {
                conn.Open();
                var cmd = new SqlCommand("SELECT * FROM Trips WHERE TripId = @Id", conn);
                cmd.Parameters.AddWithValue("@Id", id);

                var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    trip = new Trip
                    {
                        TripId = (int)reader["TripId"],
                        Destination = reader["Destination"].ToString(),
                        Country = reader["Country"].ToString(),
                        StartDate = (DateTime)reader["StartDate"],
                        EndDate = (DateTime)reader["EndDate"],
                        Price = (decimal)reader["Price"],
                        AvailableRooms = (int)reader["AvailableRooms"],
                        Category = reader["Category"].ToString(),
                        MinAge = reader["MinAge"] as int?,
                        Description = reader["Description"].ToString()
                    };
                }
                conn.Close();
            }
            return View(trip);
        }

        [HttpPost]
        public IActionResult EditTrip(Trip trip)
        {
            if (!AuthHelper.IsAdmin(HttpContext))
                return RedirectToAction("Login", "Account");

            if (!ModelState.IsValid)
                return View(trip);

            using (var conn = new SqlConnection(_connStr))
            {
                conn.Open();

                int oldRooms;
                using (var oldCmd = new SqlCommand(
                    "SELECT AvailableRooms FROM Trips WHERE TripId = @id", conn))
                {
                    oldCmd.Parameters.AddWithValue("@id", trip.TripId);
                    oldRooms = (int)oldCmd.ExecuteScalar();
                }

                var cmd = new SqlCommand(@"
                    UPDATE Trips SET
                        Destination=@Destination,
                        Country=@Country,
                        StartDate=@StartDate,
                        EndDate=@EndDate,
                        Price=@Price,
                        AvailableRooms=@Rooms,
                        Category=@Category,
                        MinAge=@MinAge,
                        Description=@Description
                    WHERE TripId=@Id", conn);

                cmd.Parameters.AddWithValue("@Id", trip.TripId);
                cmd.Parameters.AddWithValue("@Destination", trip.Destination);
                cmd.Parameters.AddWithValue("@Country", trip.Country);
                cmd.Parameters.AddWithValue("@StartDate", trip.StartDate);
                cmd.Parameters.AddWithValue("@EndDate", trip.EndDate);
                cmd.Parameters.AddWithValue("@Price", trip.Price);
                cmd.Parameters.AddWithValue("@Rooms", trip.AvailableRooms);
                cmd.Parameters.AddWithValue("@Category", trip.Category);
                cmd.Parameters.AddWithValue("@MinAge", (object?)trip.MinAge ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Description", trip.Description);

                cmd.ExecuteNonQuery();

                if (trip.AvailableRooms > oldRooms)
                {
                    try
                    {
                        var mailCmd = new SqlCommand(@"
                            SELECT TOP 1 u.Email
                            FROM WaitingList w
                            JOIN Users u ON w.UserId = u.UserId
                            WHERE w.TripId = @tid
                            ORDER BY w.JoinDate", conn);

                        mailCmd.Parameters.AddWithValue("@tid", trip.TripId);
                        var email = mailCmd.ExecuteScalar()?.ToString();

                        if (!string.IsNullOrEmpty(email))
                        {
                            EmailHelper.Send(
                                email,
                                "Seat Available!",
                                "A seat is now available for a trip you are waiting for. Please log in and complete your booking."
                            );
                        }
                    }
                    catch { }
                }

                return RedirectToAction("Trips");
            }
        }

        [HttpPost]
        public IActionResult DeleteTrip(int id)
        {
            if (!AuthHelper.IsAdmin(HttpContext))
                return RedirectToAction("Login", "Account");

            using (var conn = new SqlConnection(_connStr))
            {
                conn.Open();

                var checkCmd = new SqlCommand("SELECT COUNT(*) FROM Bookings WHERE TripId=@id", conn);
                checkCmd.Parameters.AddWithValue("@id", id);

                if ((int)checkCmd.ExecuteScalar() > 0)
                {
                    TempData["Error"] = "לא ניתן למחוק טיול שיש לו הזמנות.";
                    return RedirectToAction("Trips");
                }

                new SqlCommand("DELETE FROM TripImages WHERE TripId=@id", conn)
                { Parameters = { new SqlParameter("@id", id) } }.ExecuteNonQuery();

                new SqlCommand("DELETE FROM WaitingList WHERE TripId=@id", conn)
                { Parameters = { new SqlParameter("@id", id) } }.ExecuteNonQuery();

                new SqlCommand("DELETE FROM Trips WHERE TripId=@id", conn)
                { Parameters = { new SqlParameter("@id", id) } }.ExecuteNonQuery();

                TempData["Success"] = "הטיול נמחק בהצלחה.";
                return RedirectToAction("Trips");
            }
        }

        [HttpGet]
        public IActionResult Discount(int id)
        {
            if (!AuthHelper.IsAdmin(HttpContext))
                return RedirectToAction("Login", "Account");

            ViewBag.TripId = id;
            return View();
        }

        [HttpPost]
        public IActionResult Discount(int tripId, decimal newPrice, DateTime endDate)
        {
            if (!AuthHelper.IsAdmin(HttpContext))
                return RedirectToAction("Login", "Account");

            if (endDate > DateTime.Now.AddDays(7))
            {
                TempData["Error"] = "הנחה יכולה להיות מקסימום לשבוע אחד.";
                return RedirectToAction("Trips");
            }

            using (var conn = new SqlConnection(_connStr))
            {
                conn.Open();
                var cmd = new SqlCommand(@"
                    UPDATE Trips
                    SET OldPrice = CASE WHEN OldPrice IS NULL THEN Price ELSE OldPrice END,
                        Price = @newPrice,
                        DiscountEndDate = @endDate
                    WHERE TripId = @id", conn);

                cmd.Parameters.AddWithValue("@newPrice", newPrice);
                cmd.Parameters.AddWithValue("@endDate", endDate);
                cmd.Parameters.AddWithValue("@id", tripId);
                cmd.ExecuteNonQuery();
            }

            TempData["Success"] = "ההנחה עודכנה בהצלחה.";
            return RedirectToAction("Trips");
        }

        [HttpPost]
        public IActionResult SendDepartureReminders()
        {
            if (!AuthHelper.IsAdmin(HttpContext))
                return RedirectToAction("Login", "Account");

            using var conn = new SqlConnection(_connStr);
            conn.Open();

            var cmd = new SqlCommand(@"
        SELECT u.Email, t.Destination, t.StartDate
        FROM Bookings b
        JOIN Trips t ON b.TripId = t.TripId
        JOIN Users u ON b.UserId = u.UserId
        WHERE b.Status = 'Active'
        AND CAST(t.StartDate AS DATE) = CAST(DATEADD(day, 5, GETDATE()) AS DATE)
    ", conn);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var email = reader["Email"].ToString();
                var destination = reader["Destination"].ToString();
                var startDate = ((DateTime)reader["StartDate"]).ToShortDateString();

                EmailHelper.Send(
                    email,
                    "Upcoming Trip Reminder",
                    $"Reminder: Your trip to {destination} starts on {startDate}."
                );
            }

            TempData["Success"] = "Reminders sent successfully.";
            return RedirectToAction("Trips");
        }

        // ======================= USERS MANAGEMENT =======================

        public IActionResult Users()
        {
            if (!AuthHelper.IsAdmin(HttpContext))
                return RedirectToAction("Login", "Account");

            var users = new List<User>();

            using (var conn = new SqlConnection(_connStr))
            {
                conn.Open();
                var cmd = new SqlCommand(
                    "SELECT UserId, FullName, Email, Status FROM Users ORDER BY UserId DESC", conn);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    users.Add(new User
                    {
                        UserId = (int)reader["UserId"],
                        FullName = reader["FullName"].ToString(),
                        Email = reader["Email"].ToString(),
                        Status = reader["Status"].ToString()
                    });
                }
                conn.Close();
            }

            return View(users);
        }

        [HttpGet]
        public IActionResult AddUser()
        {
            if (!AuthHelper.IsAdmin(HttpContext))
                return RedirectToAction("Login", "Account");

            return View(new User());
        }

        [HttpPost]
        public IActionResult AddUser(string fullName, string email, string password)
        {
            if (!AuthHelper.IsAdmin(HttpContext))
                return RedirectToAction("Login", "Account");

            if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                TempData["Error"] = "All fields are required.";
                return RedirectToAction("AddUser");
            }

            string hashed = PasswordHelper.Hash(password);

            using (var conn = new SqlConnection(_connStr))
            {
                conn.Open();

                // בדיקה שלא קיים אימייל
                var checkCmd = new SqlCommand("SELECT COUNT(*) FROM Users WHERE Email=@e", conn);
                checkCmd.Parameters.AddWithValue("@e", email);
                int exists = (int)checkCmd.ExecuteScalar();
                if (exists > 0)
                {
                    TempData["Error"] = "Email already exists.";
                    return RedirectToAction("AddUser");
                }

                var cmd = new SqlCommand(@"
            INSERT INTO Users (FullName, Email, PasswordHash, Role, Status)
            VALUES (@n, @e, @p, 'User', 'Active')", conn);

                cmd.Parameters.AddWithValue("@n", fullName);
                cmd.Parameters.AddWithValue("@e", email);
                cmd.Parameters.AddWithValue("@p", hashed);

                cmd.ExecuteNonQuery();
                conn.Close();
            }

            TempData["Success"] = "User added successfully.";
            return RedirectToAction("Users");
        }

        [HttpPost]
        public IActionResult ToggleUserStatus(int userId)
        {
            if (!AuthHelper.IsAdmin(HttpContext))
                return RedirectToAction("Login", "Account");

            using (var conn = new SqlConnection(_connStr))
            {
                conn.Open();
                var cmd = new SqlCommand(@"
            UPDATE Users
            SET Status = CASE WHEN Status='Active' THEN 'Blocked' ELSE 'Active' END
            WHERE UserId=@uid", conn);

                cmd.Parameters.AddWithValue("@uid", userId);
                cmd.ExecuteNonQuery();
                conn.Close();
            }

            TempData["Success"] = "User status updated.";
            return RedirectToAction("Users");
        }

        // Removing users (לפי דרישה) – נעשה בצורה בטוחה:
        // אם יש הזמנות -> לא מוחקים פיזית, אלא Status='Blocked' (זה עדיין "removing" מהמערכת).
        [HttpPost]
        public IActionResult RemoveUser(int userId)
        {
            if (!AuthHelper.IsAdmin(HttpContext))
                return RedirectToAction("Login", "Account");

            using (var conn = new SqlConnection(_connStr))
            {
                conn.Open();

                var hasBookingsCmd = new SqlCommand("SELECT COUNT(*) FROM Bookings WHERE UserId=@uid", conn);
                hasBookingsCmd.Parameters.AddWithValue("@uid", userId);
                int bookings = (int)hasBookingsCmd.ExecuteScalar();

                if (bookings > 0)
                {
                    // Soft remove
                    var blockCmd = new SqlCommand("UPDATE Users SET Status='Blocked' WHERE UserId=@uid", conn);
                    blockCmd.Parameters.AddWithValue("@uid", userId);
                    blockCmd.ExecuteNonQuery();

                    TempData["Error"] = "User has bookings, so the account was blocked instead of deleted.";
                    return RedirectToAction("Users");
                }

                // אם אין הזמנות — מוחקים פיזית (removing)
                var delCmd = new SqlCommand("DELETE FROM Users WHERE UserId=@uid", conn);
                delCmd.Parameters.AddWithValue("@uid", userId);
                delCmd.ExecuteNonQuery();

                conn.Close();
            }

            TempData["Success"] = "User removed successfully.";
            return RedirectToAction("Users");
        }

        // Booking history של משתמש
        public IActionResult UserBookings(int id)
        {
            if (!AuthHelper.IsAdmin(HttpContext))
                return RedirectToAction("Login", "Account");

            var list = new List<TravelAgency.ViewModel.BookingViewModel>();

            using (var conn = new SqlConnection(_connStr))
            {
                conn.Open();
                var cmd = new SqlCommand(@"
            SELECT t.Destination, t.Country, t.StartDate, b.Status
            FROM Bookings b
            JOIN Trips t ON b.TripId = t.TripId
            WHERE b.UserId=@uid
            ORDER BY t.StartDate DESC", conn);

                cmd.Parameters.AddWithValue("@uid", id);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(new TravelAgency.ViewModel.BookingViewModel
                    {
                        Destination = reader["Destination"].ToString(),
                        Country = reader["Country"].ToString(),
                        StartDate = (DateTime)reader["StartDate"],
                        Status = reader["Status"].ToString()
                    });
                }
                conn.Close();
            }

            return View(list);
        }

        public IActionResult Index()
        {
            return View();
        }
    }
}
