using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using TravelAgency.Models;

namespace TravelAgency.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly string _connStr;

    public HomeController(ILogger<HomeController> logger, IConfiguration config)
    {
        _logger = logger;
        _connStr = config.GetConnectionString("DefaultConnection");
    }

    public IActionResult Index()
    {
        int totalTrips = 0;

        var popularTrips = new List<Trip>();         
        var featuredPackages = new List<Trip>();      
        var galleryImages = new List<string>();      
        var siteReviews = new List<dynamic>();      

        using var conn = new SqlConnection(_connStr);
        conn.Open();

        using (var cmd = new SqlCommand(@"SELECT COUNT(*) FROM Trips WHERE ISNULL(IsHidden,0)=0", conn))
        {
            totalTrips = Convert.ToInt32(cmd.ExecuteScalar());
        }

        using (var cmd = new SqlCommand(@"
            SELECT TOP 3 t.*
            FROM Trips t
            WHERE ISNULL(t.IsHidden,0)=0
            ORDER BY (SELECT COUNT(*) FROM Bookings b WHERE b.TripId=t.TripId) DESC, t.TripId DESC;", conn))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                popularTrips.Add(ReadTrip(r));
            }
        }

        using (var cmd = new SqlCommand(@"
            SELECT TOP 3 *
            FROM Trips
            WHERE ISNULL(IsHidden,0)=0
              AND OldPrice IS NOT NULL
              AND DiscountEndDate IS NOT NULL
              AND DiscountEndDate > GETDATE()
            ORDER BY DiscountEndDate ASC;", conn))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                featuredPackages.Add(ReadTrip(r));
            }
        }

        if (featuredPackages.Count < 3)
        {
            int need = 3 - featuredPackages.Count;
            using var cmd = new SqlCommand(@"
                SELECT TOP (@n) *
                FROM Trips
                WHERE ISNULL(IsHidden,0)=0
                ORDER BY TripId DESC;", conn);
            cmd.Parameters.AddWithValue("@n", need);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                featuredPackages.Add(ReadTrip(r));
            }
        }

        using (var cmd = new SqlCommand(@"
            SELECT TOP 5 ti.ImagePath
            FROM TripImages ti
            JOIN Trips t ON t.TripId = ti.TripId
            WHERE ISNULL(t.IsHidden,0)=0
            ORDER BY NEWID();", conn))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                var p = r["ImagePath"]?.ToString() ?? "";
                if (!string.IsNullOrWhiteSpace(p)) galleryImages.Add(p);
            }
        }

        if (galleryImages.Count == 0)
        {
            using var cmd = new SqlCommand(@"
                SELECT TOP 5 ImagePath
                FROM Trips
                WHERE ISNULL(IsHidden,0)=0 AND ImagePath IS NOT NULL
                ORDER BY NEWID();", conn);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var p = r["ImagePath"]?.ToString() ?? "";
                if (!string.IsNullOrWhiteSpace(p)) galleryImages.Add(p);
            }
        }

        using (var cmd = new SqlCommand(@"
            SELECT TOP 6 sr.Rating, sr.Comment, sr.CreatedAt, u.FullName
            FROM SiteReviews sr
            JOIN Users u ON sr.UserId = u.UserId
            ORDER BY sr.CreatedAt DESC;", conn))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                siteReviews.Add(new
                {
                    Rating = (int)r["Rating"],
                    Comment = r["Comment"] == DBNull.Value ? "" : r["Comment"]?.ToString() ?? "",
                    CreatedAt = (DateTime)r["CreatedAt"],
                    FullName = r["FullName"]?.ToString() ?? ""
                });
            }
        }

        ViewBag.TotalTrips = totalTrips;
        ViewBag.PopularTrips = popularTrips;
        ViewBag.FeaturedPackages = featuredPackages;
        ViewBag.GalleryImages = galleryImages;
        ViewBag.SiteReviews = siteReviews;

        return View();
    }

    public IActionResult Privacy() => View();

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }

    private static Trip ReadTrip(SqlDataReader reader)
    {
        return new Trip
        {
            TripId = (int)reader["TripId"],
            PackageName = reader["PackageName"] == DBNull.Value ? "" : reader["PackageName"].ToString(),
            Destination = reader["Destination"]?.ToString() ?? "",
            Country = reader["Country"]?.ToString() ?? "",
            StartDate = (DateTime)reader["StartDate"],
            EndDate = (DateTime)reader["EndDate"],
            Price = (decimal)reader["Price"],
            AvailableRooms = (int)reader["AvailableRooms"],
            Category = reader["Category"] == DBNull.Value ? "" : reader["Category"].ToString(),
            MinAge = reader["MinAge"] == DBNull.Value ? null : (int?)reader["MinAge"],
            ImagePath = reader["ImagePath"] == DBNull.Value ? null : reader["ImagePath"]?.ToString(),
            OldPrice = reader["OldPrice"] == DBNull.Value ? null : (decimal?)reader["OldPrice"],
            DiscountEndDate = reader["DiscountEndDate"] == DBNull.Value ? null : (DateTime?)reader["DiscountEndDate"],
            CancellationDays = reader["CancellationDays"] == DBNull.Value ? 0 : Convert.ToInt32(reader["CancellationDays"])
        };
    }
}
