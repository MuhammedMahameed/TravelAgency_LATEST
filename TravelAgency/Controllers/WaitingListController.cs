using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using TravelAgency.Helpers;

namespace TravelAgency.Controllers;

public class WaitingListController : Controller
{
    private readonly string _connStr;

    public WaitingListController(IConfiguration config)
    {
        _connStr = config.GetConnectionString("DefaultConnection");
    }

    [HttpPost]
    public IActionResult join(int tripId)
    {
        if(!AuthHelper.IsLoggedIn(HttpContext))
            return RedirectToAction("Login","Account");
        
        int userId = HttpContext.Session.GetInt32("UserId").Value;

        using (SqlConnection conn = new SqlConnection(_connStr))
        {
            conn.Open();

            try
            {
                var cmd = new SqlCommand(@"INSERT INTO WaitingList (TripId,UserId) VALUES (@tid,@uid)", conn);
                cmd.Parameters.AddWithValue("@tid", tripId);
                cmd.Parameters.AddWithValue("@uid", userId);
                cmd.ExecuteNonQuery();
            }
            catch
            {
                return Content("You are already in the waiting list");
            }
            conn.Close();
            
        }
        return RedirectToAction("Status", new{tripId});
    }

    public IActionResult Status(int tripId)
    {
        if(!AuthHelper.IsLoggedIn(HttpContext))
            return RedirectToAction("Login", "Account");
        int userId = HttpContext.Session.GetInt32("UserId").Value;
        using (SqlConnection conn = new SqlConnection(_connStr))
        {
            conn.Open();
            var cmd = new SqlCommand(@"SELECT COUNT(*) FROM WaitingList"+
                      " WHERE TripId=@tid AND JoinDate <=" +
                            " (SELECT JoinDate FROM WaitingList" +
                             " WHERE TripId=@tid AND UserId=@uid)", conn);
            
            cmd.Parameters.AddWithValue("@tid", tripId);
            cmd.Parameters.AddWithValue("@uid", userId);
            int position =  (int)cmd.ExecuteScalar();
            ViewBag.position = position;
            return View();
        }
    }

    [HttpPost]
    public IActionResult Leave(int tripId)
    {
        if (!AuthHelper.IsLoggedIn(HttpContext))
        {
            return RedirectToAction("Login", "Account");
        }
        int userId = HttpContext.Session.GetInt32("UserId").Value;

        using (SqlConnection conn = new SqlConnection(_connStr))
        {
            conn.Open();
            var cmd = new SqlCommand(@"DELETE FROM WaitingList WHERE TripId=@tid AND UserId = @uid", conn);
            cmd.Parameters.AddWithValue("@tid", tripId);
            cmd.Parameters.AddWithValue("@uid", userId);
            cmd.ExecuteNonQuery();
            conn.Close();
        }
        return RedirectToAction("Gallery", "Trips");
    }
    // GET
    public IActionResult Index()
    {
        return View();
    }
}