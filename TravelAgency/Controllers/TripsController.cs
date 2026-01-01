using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using TravelAgency.Models;
using TravelAgency.ViewModel;

namespace TravelAgency.Controllers;

public class TripsController : Controller
{
    private readonly string _connStr;

    public TripsController(IConfiguration configuration)
    {
        _connStr = configuration.GetConnectionString("DefaultConnection");
    }

    public IActionResult Gallery(string search, string category, string sort, decimal? minPrice, decimal? maxPrice)
    {
        var trips = new List<Trip>();
        using (SqlConnection conn = new SqlConnection(_connStr))
        {
            conn.Open();
            var sql = @"SELECT * FROM Trips WHERE 1=1 ";
            if (!string.IsNullOrEmpty(search))
            {
                sql += " AND (Destination LIKE @search OR Country LIKE @search)";
            }

            if (!string.IsNullOrEmpty(category))
            {
                sql += " AND (Category LIKE @category)";
            }

            if (minPrice != null)
            {
                sql += " AND Price >= @minPrice";
            }

            if (maxPrice != null)
            {
                sql += " AND Price <= @maxPrice";
            }

            switch (sort)
            {
                case "price_asc":
                    sql += " ORDER BY Price ASC";
                    break;

                case "price_desc":
                    sql += " ORDER BY Price DESC";
                    break;

                case "date":
                    sql += " ORDER BY StartDate ASC";
                    break;

                case "popular":
                    sql += @" ORDER BY 
            (SELECT COUNT(*) FROM Bookings b WHERE b.TripId = Trips.TripId) DESC";
                    break;

                default:
                    sql += " ORDER BY TripId DESC";
                    break;
            }

            using (SqlCommand command = new SqlCommand(sql, conn))
            {
                if (!string.IsNullOrEmpty(search))
                {
                    command.Parameters.AddWithValue("@search", "%" + search + "%");
                }

                if (!string.IsNullOrEmpty(category))
                {
                    command.Parameters.AddWithValue("@category", category);
                }

                if (minPrice != null)
                {
                    command.Parameters.AddWithValue("@minPrice", minPrice.Value);
                }

                if (maxPrice != null)
                {
                    command.Parameters.AddWithValue("@maxPrice", maxPrice.Value);
                }

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        trips.Add(new Trip
                        {
                            TripId = (int)reader["TripId"],
                            Destination = reader["Destination"].ToString(),
                            Country = reader["Country"].ToString(),
                            Price = (decimal)reader["Price"],

                            OldPrice = reader["OldPrice"] == DBNull.Value ? null : (decimal?)reader["OldPrice"],
                            DiscountEndDate = reader["DiscountEndDate"] == DBNull.Value ? null : (DateTime?)reader["DiscountEndDate"],

                            AvailableRooms = (int)reader["AvailableRooms"],
                            Category = reader["Category"].ToString(),
                            StartDate = (DateTime)reader["StartDate"],
                            EndDate = (DateTime)reader["EndDate"],
                            ImagePath = reader["ImagePath"] == DBNull.Value
                                ? null
                                : reader["ImagePath"].ToString()
                        });
                    }
                }
            }

            var waitingCounts = new Dictionary<int, int>();
            using (var countCmd =
                   new SqlCommand(@"SELECT TripId, COUNT(*) As Cnt FROM WaitingList Group BY TripId", conn))
            {
                using (var r = countCmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        waitingCounts[(int)r["TripId"]] = (int)r["Cnt"];
                    }
                }
            }

            var myPositions = new Dictionary<int, int>();
            var uidObj = HttpContext.Session.GetInt32("UserId");
            if (uidObj != null)
            {
                int userId = uidObj.Value;

                using var posCmd =
                    new SqlCommand(
                        "SELECT TripId, Pos FROM (SELECT TripId, UserId, ROW_NUMBER() OVER(PARTITION BY TripId ORDER BY JoinDate) AS Pos FROM WaitingList) w WHERE w.UserId=@uid",
                        conn);

                posCmd.Parameters.AddWithValue("@uid", userId);

                using var r2 = posCmd.ExecuteReader();
                while (r2.Read())
                    myPositions[(int)r2["TripId"]] = Convert.ToInt32(r2["Pos"]);
            }

            // 4) Build view model
            var vm = trips.Select(t => new TripGalleryItemVM
            {
                Trip = t,
                WaitingCount = waitingCounts.TryGetValue(t.TripId, out var c) ? c : 0,
                MyPosition = myPositions.TryGetValue(t.TripId, out var p) ? p : (int?)null
            }).ToList();

            // New: detect which trips the current user already booked
            if (uidObj != null)
            {
                int userId = uidObj.Value;
                var bookedCmd = new SqlCommand(@"SELECT BookingId, TripId FROM Bookings WHERE UserId=@uid AND Status='Active'", conn);
                bookedCmd.Parameters.AddWithValue("@uid", userId);
                using var br = bookedCmd.ExecuteReader();
                var bookings = new Dictionary<int, int>();
                while (br.Read())
                {
                    bookings[(int)br["TripId"]] = (int)br["BookingId"];
                }

                foreach (var item in vm)
                {
                    if (bookings.TryGetValue(item.Trip.TripId, out var bid))
                    {
                        item.IsBookedByMe = true;
                        item.MyBookingId = bid;
                    }
                }
            }

            // ===== ADDED: fetch general site reviews for Gallery page =====
            var siteReviews = new List<dynamic>();
            using (var rcmd = new SqlCommand(@"
                SELECT TOP 6 sr.Rating, sr.Comment, sr.CreatedAt, u.FullName
                FROM SiteReviews sr
                JOIN Users u ON sr.UserId = u.UserId
                ORDER BY sr.CreatedAt DESC", conn))
            {
                using var rr = rcmd.ExecuteReader();
                while (rr.Read())
                {
                    siteReviews.Add(new
                    {
                        Rating = (int)rr["Rating"],
                        Comment = rr["Comment"] == DBNull.Value ? "" : rr["Comment"].ToString(),
                        CreatedAt = (DateTime)rr["CreatedAt"],
                        FullName = rr["FullName"].ToString()
                    });
                }
            }
            ViewBag.SiteReviews = siteReviews;
            // ===== END added section =====

            conn.Close();

            return View(vm);
        }
    }

    public IActionResult Details(int id)
    {
        Trip trip;
        using (SqlConnection conn = new SqlConnection(_connStr))
        {
            conn.Open();
            var cmd = new SqlCommand(@"SELECT * FROM Trips WHERE TripId = @id", conn);
            cmd.Parameters.AddWithValue("@id", id);

            using (var reader = cmd.ExecuteReader())
            {
                if (!reader.Read())
                    return NotFound();

                trip = new Trip
                {
                    TripId = (int)reader["TripId"],
                    Destination = reader["Destination"].ToString(),
                    Country = reader["Country"].ToString(),
                    Category = reader["Category"].ToString(),
                    StartDate = (DateTime)reader["StartDate"],
                    EndDate = (DateTime)reader["EndDate"],
                    Price = (decimal)reader["Price"],

                    OldPrice = reader["OldPrice"] == DBNull.Value ? null : (decimal?)reader["OldPrice"],
                    DiscountEndDate = reader["DiscountEndDate"] == DBNull.Value ? null : (DateTime?)reader["DiscountEndDate"],

                    AvailableRooms = (int)reader["AvailableRooms"],
                    Description = reader["Description"].ToString(),
                    ImagePath = reader["ImagePath"] == DBNull.Value
                        ? null
                        : reader["ImagePath"].ToString()
                };
            }

            // detect if current user already has an active booking for this trip
            var uidObj = HttpContext.Session.GetInt32("UserId");
            if (uidObj != null)
            {
                var checkCmd = new SqlCommand("SELECT BookingId FROM Bookings WHERE TripId=@tid AND UserId=@uid AND Status='Active'", conn);
                checkCmd.Parameters.AddWithValue("@tid", id);
                checkCmd.Parameters.AddWithValue("@uid", uidObj.Value);
                var obj = checkCmd.ExecuteScalar();
                if (obj != null && obj != DBNull.Value)
                {
                    ViewBag.IsBooked = true;
                    ViewBag.MyBookingId = (int)obj;
                }
                else
                {
                    ViewBag.IsBooked = false;
                }

                // also detect if there are waiting entries for this trip (for details view)
                var waitCountCmd = new SqlCommand("SELECT COUNT(*) FROM WaitingList WHERE TripId=@tid", conn);
                waitCountCmd.Parameters.AddWithValue("@tid", id);
                var wcnt = (int)waitCountCmd.ExecuteScalar();
                ViewBag.HasWaiting = wcnt > 0;

                // detect if current user is in waiting list for this trip and their position
                var userInWaitCmd = new SqlCommand("SELECT COUNT(*) FROM WaitingList WHERE TripId=@tid AND UserId=@uid", conn);
                userInWaitCmd.Parameters.AddWithValue("@tid", id);
                userInWaitCmd.Parameters.AddWithValue("@uid", uidObj.Value);
                var inCount = (int)userInWaitCmd.ExecuteScalar();
                if (inCount > 0)
                {
                    ViewBag.IsInWaiting = true;

                    // compute position
                    var posCmd = new SqlCommand(@"
                        SELECT COUNT(*) FROM WaitingList w
                        WHERE w.TripId = @tid AND w.JoinDate <= (
                            SELECT JoinDate FROM WaitingList WHERE TripId=@tid AND UserId=@uid
                        )", conn);
                    posCmd.Parameters.AddWithValue("@tid", id);
                    posCmd.Parameters.AddWithValue("@uid", uidObj.Value);
                    var pos = (int)posCmd.ExecuteScalar();
                    ViewBag.WaitPosition = pos;
                }
                else
                {
                    ViewBag.IsInWaiting = false;
                    ViewBag.WaitPosition = null;
                }
            }
            else
            {
                ViewBag.IsBooked = false;
                ViewBag.HasWaiting = false;
            }

            conn.Close();
        }

        return View(trip);
    }

    // GET
    public IActionResult Index()
    {
        return View();
    }
}
