using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using TravelAgency.Models;
using TravelAgency.Helpers;
using Microsoft.AspNetCore.Http;

namespace TravelAgency.Controllers
{
    public class ReviewsController : Controller
    {
        private readonly string _connStr;

        public ReviewsController(IConfiguration config)
        {
            _connStr = config.GetConnectionString("DefaultConnection");
        }

        [HttpPost]
        public IActionResult Add(int tripId, int rating, string comment)
        {
            if (!AuthHelper.IsLoggedIn(HttpContext))
                return RedirectToAction("Login", "Account");

            int userId = HttpContext.Session.GetInt32("UserId").Value;

            using (SqlConnection conn = new SqlConnection(_connStr))
            {
                conn.Open();
                var checkCmd = new SqlCommand(@"
                    SELECT COUNT(*) 
                    FROM Bookings 
                    WHERE UserId=@uid AND TripId=@tid AND Status='Active'", conn);
                checkCmd.Parameters.AddWithValue("@uid", userId);
                checkCmd.Parameters.AddWithValue("@tid", tripId);

                int hasBooking = (int)checkCmd.ExecuteScalar();
                if (hasBooking == 0)
                {
                    TempData["Error"] = "You can only rate trips you booked.";
                    return RedirectToAction("Details", "Trips", new { id = tripId });
                }

                var cmd = new SqlCommand(@"
                    INSERT INTO Reviews (TripId, UserId, Rating, Comment)
                    VALUES (@tid, @uid, @r, @c)", conn);
                cmd.Parameters.AddWithValue("@tid", tripId);
                cmd.Parameters.AddWithValue("@uid", userId);
                cmd.Parameters.AddWithValue("@r", rating);
                cmd.Parameters.AddWithValue("@c", (object?)comment ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }

            TempData["Success"] = "Review added successfully!";
            return RedirectToAction("Details", "Trips", new { id = tripId });
        }

        [HttpPost]
        public IActionResult Delete(int reviewId, int tripId)
        {
            if (!AuthHelper.IsAdmin(HttpContext))
                return RedirectToAction("Login", "Account");

            using (SqlConnection conn = new SqlConnection(_connStr))
            {
                conn.Open();
                var cmd = new SqlCommand("DELETE FROM Reviews WHERE ReviewId=@rid", conn);
                cmd.Parameters.AddWithValue("@rid", reviewId);
                cmd.ExecuteNonQuery();
            }

            TempData["Success"] = "Review deleted successfully!";

            var referer = Request.Headers["Referer"].ToString();
            if (!string.IsNullOrEmpty(referer) && referer.Contains("/Admin/EditTrip", System.StringComparison.OrdinalIgnoreCase))
            {
                return RedirectToAction("EditTrip", "Admin", new { id = tripId });
            }

            return RedirectToAction("Details", "Trips", new { id = tripId });
        }
    }
}
