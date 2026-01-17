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

    [HttpGet]
    public IActionResult JoinInfo(int tripId)
    {
        return RedirectToAction("Status", new { tripId });
    }

    [HttpPost]
    public IActionResult Join(int tripId)
    {
        if (!AuthHelper.IsLoggedIn(HttpContext))
            return RedirectToAction("Login", "Account");

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
                TempData["Error"] = "You are already in the waiting list.";
                return RedirectToAction("Details", "Trips", new { id = tripId });
            }
        }

        TempData["Success"] = "You joined the waiting list.";
        return RedirectToAction("Status", new { tripId });
    }

    public IActionResult Status(int tripId)
    {
        if (!AuthHelper.IsLoggedIn(HttpContext))
            return RedirectToAction("Login", "Account");

        int userId = HttpContext.Session.GetInt32("UserId").Value;

        using (SqlConnection conn = new SqlConnection(_connStr))
        {
            conn.Open();

            var cntCmd = new SqlCommand(@"SELECT COUNT(*) FROM WaitingList WHERE TripId=@tid", conn);
            cntCmd.Parameters.AddWithValue("@tid", tripId);
            int waitingCount = Convert.ToInt32(cntCmd.ExecuteScalar());

            var inWaitCmd = new SqlCommand(@"SELECT COUNT(*) FROM WaitingList WHERE TripId=@tid AND UserId=@uid", conn);
            inWaitCmd.Parameters.AddWithValue("@tid", tripId);
            inWaitCmd.Parameters.AddWithValue("@uid", userId);
            bool isInWaiting = Convert.ToInt32(inWaitCmd.ExecuteScalar()) > 0;

            int? position = null;
            if (isInWaiting)
            {
                var posCmd = new SqlCommand(@"
                    SELECT COUNT(*)
                    FROM WaitingList
                    WHERE TripId=@tid AND JoinDate <= (
                        SELECT JoinDate FROM WaitingList WHERE TripId=@tid AND UserId=@uid
                    )", conn);

                posCmd.Parameters.AddWithValue("@tid", tripId);
                posCmd.Parameters.AddWithValue("@uid", userId);
                position = Convert.ToInt32(posCmd.ExecuteScalar());
            }

            var etaCmd = new SqlCommand(@"
                SELECT MIN(ExpirationAt)
                FROM WaitingList
                WHERE TripId=@tid AND ExpirationAt IS NOT NULL AND GETDATE() < ExpirationAt", conn);
            etaCmd.Parameters.AddWithValue("@tid", tripId);
            var etaObj = etaCmd.ExecuteScalar();
            DateTime? eta = etaObj == null || etaObj == DBNull.Value ? null : (DateTime?)etaObj;

            ViewBag.TripId = tripId;
            ViewBag.Position = position;
            ViewBag.IsInWaiting = isInWaiting;
            ViewBag.WaitingCount = waitingCount;
            ViewBag.EstimatedAvailableAt = eta;

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
        }

        TempData["Success"] = "You left the waiting list.";
        return RedirectToAction("Details", "Trips", new { id = tripId });
    }

    public IActionResult Index()
    {
        return View();
    }
}