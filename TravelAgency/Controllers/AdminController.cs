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
                        AvailableRooms = (int)reader["AvailableRooms"],
                        ImagePath = reader["ImagePath"] == DBNull.Value ? null : reader["ImagePath"].ToString()

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

            var allowedCategories = new[] { "Family", "Honeymoon", "Adventure", "Cruise", "Luxury" };
            if (string.IsNullOrWhiteSpace(trip.Category) ||
                !allowedCategories.Contains(trip.Category, StringComparer.OrdinalIgnoreCase))
            {
                TempData["Error"] = "Please select a valid category.";
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
            var gallery = new List<TripImage>();

            var reviews = new List<dynamic>();

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
                        Description = reader["Description"].ToString(),
                        ImagePath = reader["ImagePath"] == DBNull.Value ? null : reader["ImagePath"].ToString()
                    };
                }
                reader.Close();

                // load gallery images
                var imgCmd = new SqlCommand("SELECT ImageId, ImagePath FROM TripImages WHERE TripId=@id", conn);
                imgCmd.Parameters.AddWithValue("@id", id);
                using var r2 = imgCmd.ExecuteReader();
                while (r2.Read())
                {
                    gallery.Add(new TripImage
                    {
                        ImageId = (int)r2["ImageId"],
                        TripId = id,
                        ImagePath = r2["ImagePath"].ToString()
                    });
                }
                r2.Close();

                var revCmd = new SqlCommand(@"
                    SELECT r.ReviewId, r.Rating, r.Comment, r.CreatedAt, u.FullName
                    FROM Reviews r
                    JOIN Users u ON r.UserId = u.UserId
                    WHERE r.TripId = @tid
                    ORDER BY r.CreatedAt DESC", conn);
                revCmd.Parameters.AddWithValue("@tid", id);

                using var rr = revCmd.ExecuteReader();
                while (rr.Read())
                {
                    reviews.Add(new
                    {
                        ReviewId = (int)rr["ReviewId"],
                        FullName = rr["FullName"]?.ToString() ?? "",
                        Rating = (int)rr["Rating"],
                        Comment = rr["Comment"] == DBNull.Value ? "" : rr["Comment"]?.ToString() ?? "",
                        CreatedAt = (DateTime)rr["CreatedAt"]
                    });
                }

                conn.Close();
            }

            ViewBag.Gallery = gallery;

            ViewBag.Reviews = reviews;

            return View(trip);
        }

        [HttpPost]
        public IActionResult EditTrip(Trip trip, IFormFile? imageFile, List<IFormFile>? galleryImages, int[]? deleteImageIds, int? setMainImageId)
        {
            if (!AuthHelper.IsAdmin(HttpContext))
                return RedirectToAction("Login", "Account");

            // If model is invalid, reload gallery/main image and return view so validation messages show
            if (!ModelState.IsValid)
            {
                var gallery = new List<TripImage>();
                string? existingMainPath = null;

                var reviews = new List<dynamic>();

                using (var conn = new SqlConnection(_connStr))
                {
                    conn.Open();
                    var imgCmd = new SqlCommand("SELECT ImageId, ImagePath FROM TripImages WHERE TripId=@id", conn);
                    imgCmd.Parameters.AddWithValue("@id", trip.TripId);
                    using var r2 = imgCmd.ExecuteReader();
                    while (r2.Read())
                    {
                        gallery.Add(new TripImage
                        {
                            ImageId = (int)r2["ImageId"],
                            TripId = trip.TripId,
                            ImagePath = r2["ImagePath"].ToString()
                        });
                    }
                    r2.Close();

                    // load main image path
                    var mainCmd = new SqlCommand("SELECT ImagePath FROM Trips WHERE TripId=@id", conn);
                    mainCmd.Parameters.AddWithValue("@id", trip.TripId);
                    var obj = mainCmd.ExecuteScalar();
                    existingMainPath = obj == DBNull.Value || obj == null ? null : obj.ToString();

                    var revCmd = new SqlCommand(@"
                        SELECT r.ReviewId, r.Rating, r.Comment, r.CreatedAt, u.FullName
                        FROM Reviews r
                        JOIN Users u ON r.UserId = u.UserId
                        WHERE r.TripId = @tid
                        ORDER BY r.CreatedAt DESC", conn);
                    revCmd.Parameters.AddWithValue("@tid", trip.TripId);

                    using var rr = revCmd.ExecuteReader();
                    while (rr.Read())
                    {
                        reviews.Add(new
                        {
                            ReviewId = (int)rr["ReviewId"],
                            FullName = rr["FullName"]?.ToString() ?? "",
                            Rating = (int)rr["Rating"],
                            Comment = rr["Comment"] == DBNull.Value ? "" : rr["Comment"]?.ToString() ?? "",
                            CreatedAt = (DateTime)rr["CreatedAt"]
                        });
                    }

                    conn.Close();
                }

                ViewBag.Gallery = gallery;
                ViewBag.ExistingMainPath = existingMainPath;

                ViewBag.Reviews = reviews;

                return View(trip);
            }

            // Ensure folder exists
            var imagesPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/images/trips");
            if (!Directory.Exists(imagesPath))
                Directory.CreateDirectory(imagesPath);

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

                // read existing main image path so we can delete it if replaced/removed
                string? existingMainPath = null;
                using (var mainCmd = new SqlCommand("SELECT ImagePath FROM Trips WHERE TripId=@id", conn))
                {
                    mainCmd.Parameters.AddWithValue("@id", trip.TripId);
                    var obj = mainCmd.ExecuteScalar();
                    existingMainPath = obj == DBNull.Value || obj == null ? null : obj.ToString();
                }

                // determine requested main image path (from gallery) if provided
                string? requestedMainPath = null;
                if (setMainImageId.HasValue)
                {
                    // ensure not trying to set a main image that is marked for deletion
                    if (deleteImageIds == null || !deleteImageIds.Contains(setMainImageId.Value))
                    {
                        using var smCmd = new SqlCommand("SELECT ImagePath FROM TripImages WHERE ImageId=@iid AND TripId=@tid", conn);
                        smCmd.Parameters.AddWithValue("@iid", setMainImageId.Value);
                        smCmd.Parameters.AddWithValue("@tid", trip.TripId);
                        var smObj = smCmd.ExecuteScalar();
                        requestedMainPath = smObj == DBNull.Value || smObj == null ? null : smObj.ToString();
                    }
                }

                // handle deletions of gallery images (both DB and filesystem)
                if (deleteImageIds != null && deleteImageIds.Length > 0)
                {
                    foreach (var imgId in deleteImageIds)
                    {
                        // get path
                        var pCmd = new SqlCommand("SELECT ImagePath FROM TripImages WHERE ImageId=@id", conn);
                        pCmd.Parameters.AddWithValue("@id", imgId);
                        var pathObj = pCmd.ExecuteScalar();
                        if (pathObj != null && pathObj != DBNull.Value)
                        {
                            var rel = pathObj.ToString();
                            // if this gallery image is currently set as main, clear main image
                            if (!string.IsNullOrEmpty(existingMainPath) && string.Equals(existingMainPath, rel, StringComparison.OrdinalIgnoreCase))
                            {
                                var clearCmd = new SqlCommand("UPDATE Trips SET ImagePath = NULL WHERE TripId=@id", conn);
                                clearCmd.Parameters.AddWithValue("@id", trip.TripId);
                                clearCmd.ExecuteNonQuery();
                                existingMainPath = null;
                            }

                            var full = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", rel.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                            try { if (System.IO.File.Exists(full)) System.IO.File.Delete(full); } catch { }
                        }

                        var delCmd = new SqlCommand("DELETE FROM TripImages WHERE ImageId=@id", conn);
                        delCmd.Parameters.AddWithValue("@id", imgId);
                        delCmd.ExecuteNonQuery();
                    }
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
                cmd.Parameters.AddWithValue("@Description", trip.Description ?? string.Empty);

                cmd.ExecuteNonQuery();

                // handle main image upload (replace existing)
                if (imageFile != null && imageFile.Length > 0)
                {
                    // delete previous main image file if any, but only if it's not referenced in TripImages
                    if (!string.IsNullOrEmpty(existingMainPath))
                    {
                        bool referencedInGallery = false;
                        using (var refCmd = new SqlCommand("SELECT COUNT(*) FROM TripImages WHERE ImagePath=@path", conn))
                        {
                            refCmd.Parameters.AddWithValue("@path", existingMainPath);
                            var cnt = (int)refCmd.ExecuteScalar();
                            referencedInGallery = cnt > 0;
                        }

                        if (!referencedInGallery)
                        {
                            var prevFull = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", existingMainPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                            try { if (System.IO.File.Exists(prevFull)) System.IO.File.Delete(prevFull); } catch { }
                        }
                    }

                    var fileName = Guid.NewGuid() + Path.GetExtension(imageFile.FileName);
                    var fullPath = Path.Combine(imagesPath, fileName);
                    using (var stream = new FileStream(fullPath, FileMode.Create))
                    {
                        imageFile.CopyTo(stream);
                    }

                    var dbPath = "/images/trips/" + fileName;
                    var imgUpdate = new SqlCommand("UPDATE Trips SET ImagePath=@img WHERE TripId=@id", conn);
                    imgUpdate.Parameters.AddWithValue("@img", dbPath);
                    imgUpdate.Parameters.AddWithValue("@id", trip.TripId);
                    imgUpdate.ExecuteNonQuery();

                    existingMainPath = dbPath; // update for later logic
                }
                else if (!string.IsNullOrEmpty(requestedMainPath))
                {
                    // set main image to the selected gallery image
                    // delete previous main file if it's different and not referenced by gallery
                    if (!string.IsNullOrEmpty(existingMainPath) && !string.Equals(existingMainPath, requestedMainPath, StringComparison.OrdinalIgnoreCase))
                    {
                        bool referencedInGallery = false;
                        using (var refCmd = new SqlCommand("SELECT COUNT(*) FROM TripImages WHERE ImagePath=@path", conn))
                        {
                            refCmd.Parameters.AddWithValue("@path", existingMainPath);
                            var cnt = (int)refCmd.ExecuteScalar();
                            referencedInGallery = cnt > 0;
                        }

                        if (!referencedInGallery)
                        {
                            var prevFull = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", existingMainPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                            try { if (System.IO.File.Exists(prevFull)) System.IO.File.Delete(prevFull); } catch { }
                        }
                    }

                    var setCmd = new SqlCommand("UPDATE Trips SET ImagePath=@img WHERE TripId=@id", conn);
                    setCmd.Parameters.AddWithValue("@img", requestedMainPath);
                    setCmd.Parameters.AddWithValue("@id", trip.TripId);
                    setCmd.ExecuteNonQuery();

                    existingMainPath = requestedMainPath;
                }

                else if (Request.Form["deleteMainImage"].FirstOrDefault() == "true")
                {
                    // delete main image if requested and not replaced
                    if (!string.IsNullOrEmpty(existingMainPath))
                    {
                        // delete file only if not referenced in gallery
                        bool referencedInGallery = false;
                        using (var refCmd = new SqlCommand("SELECT COUNT(*) FROM TripImages WHERE ImagePath=@path", conn))
                        {
                            refCmd.Parameters.AddWithValue("@path", existingMainPath);
                            var cnt = (int)refCmd.ExecuteScalar();
                            referencedInGallery = cnt > 0;
                        }

                        if (!referencedInGallery)
                        {
                            var prevFull = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", existingMainPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                            try { if (System.IO.File.Exists(prevFull)) System.IO.File.Delete(prevFull); } catch { }
                        }

                        var clearCmd = new SqlCommand("UPDATE Trips SET ImagePath = NULL WHERE TripId=@id", conn);
                        clearCmd.Parameters.AddWithValue("@id", trip.TripId);
                        clearCmd.ExecuteNonQuery();

                        existingMainPath = null;
                    }
                }

                // handle new gallery images
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
                        var imgCmd = new SqlCommand(@"INSERT INTO TripImages (TripId, ImagePath) VALUES (@tid, @path)", conn);
                        imgCmd.Parameters.AddWithValue("@tid", trip.TripId);
                        imgCmd.Parameters.AddWithValue("@path", dbPath);
                        imgCmd.ExecuteNonQuery();
                    }
                }

                if (trip.AvailableRooms > oldRooms)
                {
                    WaitingListHelper.ProcessTripWaitingList(_connStr, trip.TripId);
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

        // Removing users (לפי דרישה) – נעשה בצורה btוחה:
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
            SELECT t.Destination, t.Country, t.StartDate, t.ImagePath, b.Status
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
                        Status = reader["Status"].ToString(),
                        ImagePath = reader["ImagePath"] == DBNull.Value ? null : reader["ImagePath"].ToString()
                    });
                }
                conn.Close();
            }

            return View(list);
        }


        [HttpGet]
        public IActionResult WaitingList(int id)
        {
            var list = new List<dynamic>();

            using (var conn = new SqlConnection(_connStr))
            {
                conn.Open();

                var cmd = new SqlCommand(@"
            SELECT w.WaitingId, u.FullName, u.Email, w.JoinDate,
                   ROW_NUMBER() OVER (ORDER BY w.JoinDate) AS Position
            FROM WaitingList w
            JOIN Users u ON w.UserId = u.UserId
            WHERE w.TripId = @tid
            ORDER BY w.JoinDate ASC", conn);

                cmd.Parameters.AddWithValue("@tid", id);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(new
                    {
                        WaitingId = Convert.ToInt32(reader["WaitingId"]),
                        FullName = reader["FullName"].ToString(),
                        Email = reader["Email"].ToString(),
                        JoinDate = ((DateTime)reader["JoinDate"]).ToString("yyyy-MM-dd HH:mm"),
                        Position = Convert.ToInt32(reader["Position"])
                    });
                }

                conn.Close();
            }

            ViewBag.TripId = id;
            return View(list);
        }


        [HttpPost]
        public IActionResult ProcessWaitingList()
        {
            if (!AuthHelper.IsAdmin(HttpContext))
                return RedirectToAction("Login", "Account");

            using var conn = new SqlConnection(_connStr);
            conn.Open();

            // 1?? – נבדוק מי קיבל הזדמנות של 24 שעות אבל לא הזמין
            var expiredCmd = new SqlCommand(@"
        SELECT WaitingId, TripId, UserId
        FROM WaitingList
        WHERE ExpirationAt IS NOT NULL
          AND GETDATE() > ExpirationAt", conn);

            var toRemove = new List<(int waitingId, int tripId, int userId)>();
            using (var r = expiredCmd.ExecuteReader())
            {
                while (r.Read())
                {
                    toRemove.Add((
                        Convert.ToInt32(r["WaitingId"]),
                        Convert.ToInt32(r["TripId"]),
                        Convert.ToInt32(r["UserId"])
                    ));
                }
            }

            // 2?? – נסיר אותם ונשלח הודעה למי שאחריהם
            foreach (var x in toRemove)
            {
                // מחיקה
                var del = new SqlCommand("DELETE FROM WaitingList WHERE WaitingId=@id", conn);
                del.Parameters.AddWithValue("@id", x.waitingId);
                del.ExecuteNonQuery();

                // מציאת הבא בתור
                var nextCmd = new SqlCommand(@"
            SELECT TOP 1 w.WaitingId, u.Email
            FROM WaitingList w
            JOIN Users u ON w.UserId = u.UserId
            WHERE w.TripId=@tid AND (w.NotifiedAt IS NULL)
            ORDER BY w.JoinDate", conn);
                nextCmd.Parameters.AddWithValue("@tid", x.tripId);

                using var r2 = nextCmd.ExecuteReader();
                if (r2.Read())
                {
                    int nextId = Convert.ToInt32(r2["WaitingId"]);
                    string email = r2["Email"].ToString();

                    // עדכון תאריך ההתרעה + תפוגה
                    r2.Close();
                    var update = new SqlCommand(@"
                UPDATE WaitingList
                SET NotifiedAt=GETDATE(), ExpirationAt=DATEADD(hour,24,GETDATE())
                WHERE WaitingId=@id", conn);
                    update.Parameters.AddWithValue("@id", nextId);
                    update.ExecuteNonQuery();

                    // שליחת מייל
                    EmailHelper.Send(
                        email,
                        "Room now available!",
                        "A room is now available for your desired trip. You have 24 hours to book it before it’s offered to the next user."
                    );
                }
            }

            // 3?? – אם אין פעילים, נאתר את הראשונים שעדיין לא קיבלו הודעה (חדשים ברשימה)
            var freshCmd = new SqlCommand(@"
        SELECT TOP 1 w.WaitingId, u.Email
        FROM WaitingList w
        JOIN Users u ON w.UserId = u.UserId
        WHERE w.NotifiedAt IS NULL
        ORDER BY w.JoinDate", conn);

            using (var fr = freshCmd.ExecuteReader())
            {
                if (fr.Read())
                {
                    int wid = Convert.ToInt32(fr["WaitingId"]);
                    string email = fr["Email"].ToString();
                    fr.Close();

                    var update = new SqlCommand(@"
                UPDATE WaitingList
                SET NotifiedAt=GETDATE(), ExpirationAt=DATEADD(hour,24,GETDATE())
                WHERE WaitingId=@id", conn);
                    update.Parameters.AddWithValue("@id", wid);
                    update.ExecuteNonQuery();

                    EmailHelper.Send(
                        email,
                        "Room available!",
                        "You are next in line! You have 24 hours to book your trip."
                    );
                }
            }

            conn.Close();
            TempData["Success"] = "Waiting list processed successfully.";
            return RedirectToAction("Trips");
        }




        public IActionResult Index()
        {
            return View();
        }



    }
}
