using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using TravelAgency.Models;
using TravelAgency.Helpers;
namespace TravelAgency.Controllers;

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
            var cmd = new SqlCommand("select * from Trips", conn);
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
    public IActionResult AddTrip()
    {
        if (!AuthHelper.IsAdmin(HttpContext))
            return RedirectToAction("Login", "Account");
        return View(new Trip());
    }

    [HttpPost]
    public IActionResult AddTrip(Trip trip, IFormFile? imageFile)
    {
        if (!AuthHelper.IsAdmin(HttpContext))
            return RedirectToAction("Login", "Account");
        // TEMPORARY DEBUG
        if (!ModelState.IsValid)
        {
            return Content(string.Join(" | ",
                ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)));
        }

        if (imageFile != null && imageFile.Length > 0)
        {
            var fileName = Guid.NewGuid().ToString() + Path.GetExtension(imageFile.FileName);
            var imagesPath = Path.Combine(
                Directory.GetCurrentDirectory(),
                "wwwroot/images/trips"
            );

            // make sure folder exists
            if (!Directory.Exists(imagesPath))
                Directory.CreateDirectory(imagesPath);

            var fullPath = Path.Combine(imagesPath, fileName);

            using var stream = new FileStream(fullPath, FileMode.Create);
            imageFile.CopyTo(stream);

            trip.ImagePath = "/images/trips/" + fileName;
        }
        using (var conn = new SqlConnection(_connStr))
        {
            conn.Open();
            var cmd = new SqlCommand(@"INSERT INTO Trips(Destination, Country, StartDate, EndDate, Price, AvailableRooms, Category, MinAge, Description,ImagePath) VALUES (@Destination, @Country, @StartDate, @EndDate, @Price, @Rooms, @Category, @MinAge, @Description,@ImagePath)", conn);
            
            cmd.Parameters.AddWithValue("@Destination", trip.Destination);
            cmd.Parameters.AddWithValue("@Country", trip.Country);
            cmd.Parameters.AddWithValue("@StartDate", trip.StartDate);
            cmd.Parameters.AddWithValue("@EndDate", trip.EndDate);
            cmd.Parameters.AddWithValue("@Price", trip.Price);
            cmd.Parameters.AddWithValue("@Rooms", trip.AvailableRooms);
            cmd.Parameters.AddWithValue("@Category", trip.Category);
            cmd.Parameters.AddWithValue("@MinAge", (object?)trip.MinAge ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Description", trip.Description);
            cmd.Parameters.AddWithValue("@ImagePath", (object?)trip.ImagePath ?? DBNull.Value);

            cmd.ExecuteNonQuery();
            conn.Close();
        }
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
            using (SqlCommand sqlCmd = new SqlCommand("select * from Trips WHERE TripId = @Id", conn))
            {
                sqlCmd.Parameters.AddWithValue("@Id", id);
                var reader = sqlCmd.ExecuteReader();
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
        {
            return View(trip);
        }

        using (var conn = new SqlConnection(_connStr))
        {
            conn.Open();
              var cmd = new SqlCommand(@"UPDATE Trips SET Destination=@Destination, Country=@Country," +
                                       "StartDate=@StartDate, EndDate=@EndDate, Price=@Price,AvailableRooms = @Rooms, Category=@Category, MinAge=@MinAge,Description = @Description WHERE TripId = @Id", conn);
              
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

            // 1) לא מוחקים טיול שיש לו הזמנות
            var checkCmd = new SqlCommand(
                "SELECT COUNT(*) FROM Bookings WHERE TripId = @id",
                conn);
            checkCmd.Parameters.AddWithValue("@id", id);

            int bookingsCount = (int)checkCmd.ExecuteScalar();
            if (bookingsCount > 0)
            {
                TempData["Error"] = "לא ניתן למחוק טיול שיש לו הזמנות.";
                return RedirectToAction("Trips");
            }

            // 2) למחוק קודם מה-WaitingList (אם קיימים אנשים)
            var delWaitCmd = new SqlCommand(
                "DELETE FROM WaitingList WHERE TripId = @id",
                conn);
            delWaitCmd.Parameters.AddWithValue("@id", id);
            delWaitCmd.ExecuteNonQuery();

            // 3) למחוק את הטיול
            var delTripCmd = new SqlCommand(
                "DELETE FROM Trips WHERE TripId = @id",
                conn);
            delTripCmd.Parameters.AddWithValue("@id", id);

            int rows = delTripCmd.ExecuteNonQuery();
            if (rows == 0)
            {
                TempData["Error"] = "הטיול לא נמצא.";
                return RedirectToAction("Trips");
            }

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

        // הגבלה: עד שבוע מהיום
        if (endDate > DateTime.Now.AddDays(7))
        {
            TempData["Error"] = "הנחה יכולה להיות מקסימום לשבוע אחד.";
            return RedirectToAction("Trips");
        }

        using (var conn = new SqlConnection(_connStr))
        {
            conn.Open();

            // אם אין OldPrice עדיין - נשמור את המחיר הנוכחי כ-OldPrice
            var cmd = new SqlCommand(@"
            UPDATE Trips
            SET OldPrice = CASE WHEN OldPrice IS NULL THEN Price ELSE OldPrice END,
                Price = @newPrice,
                DiscountEndDate = @endDate
            WHERE TripId = @id
        ", conn);

            cmd.Parameters.AddWithValue("@newPrice", newPrice);
            cmd.Parameters.AddWithValue("@endDate", endDate);
            cmd.Parameters.AddWithValue("@id", tripId);

            cmd.ExecuteNonQuery();
            conn.Close();
        }

        TempData["Success"] = "ההנחה עודכנה בהצלחה.";
        return RedirectToAction("Trips");
    }


    // GET
    public IActionResult Index()
    {
        return View();
    }
    
    
    
    
}