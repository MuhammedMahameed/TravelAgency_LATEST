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

    public IActionResult Gallery(string search, string category, string sort)
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

            if (sort == "price_asc")
            {
                sql += " ORDER BY Price ASC";
            }
            else if (sort == "price_desc")
            {
                sql += " ORDER BY Price DESC";
            }
            else if (sort == "date")
            {
                sql += " ORDER BY StartDate ASC";
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
            var cmd = new SqlCommand(@"SELECT * FROM Trips WHERE TripId = @id",conn);
            cmd.Parameters.AddWithValue("@id", id);

            using (var reader = cmd.ExecuteReader())
            {
                if(!reader.Read())
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
                    AvailableRooms = (int)reader["AvailableRooms"],
                    Description = reader["Description"].ToString(),
                    ImagePath = reader["ImagePath"] == DBNull.Value
                        ? null
                        : reader["ImagePath"].ToString()
                };
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