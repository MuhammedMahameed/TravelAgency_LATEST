using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using TravelAgency.Helpers;

namespace TravelAgency.Controllers;

public class PaymentController : Controller
{
    private readonly string _connStr;
    
    public PaymentController(IConfiguration config)
    {
        _connStr = config.GetConnectionString("DefaultConnection");
    }

    public IActionResult Pay(int bookingId)
    {
        if (!AuthHelper.IsLoggedIn(HttpContext))
            return RedirectToAction("Login", "Account");
        ViewBag.BookingId = bookingId;
        return View();
    }

    [HttpPost]
    public IActionResult Process(int bookingId)
    {
        if (!AuthHelper.IsLoggedIn(HttpContext))
            return RedirectToAction("Login", "Account");
        using (SqlConnection connection = new SqlConnection(_connStr))
        {
            connection.Open();
            var priceCmd =
                new SqlCommand(
                    @"SELECT t.Price From Bookings b JOIN Trips t ON b.TripId = t.TripId WHERE b.BookingId = @bid",
                    connection);
            priceCmd.Parameters.AddWithValue("@bid", bookingId);
            var priceObj = priceCmd.ExecuteScalar();
            if (priceObj == null)
                return Content("Invalid Booking.");
            
            decimal amount = (decimal)priceObj;

            var payCmd =
                new SqlCommand(
                    @"INSERT INTO Payments (BookingId, Amount, Status)"
                               +" VALUES (@bid, @amount, @status)"
                    ,connection
                    );
            payCmd.Parameters.AddWithValue("@bid", bookingId);
            payCmd.Parameters.AddWithValue("@amount", amount);
            payCmd.Parameters.AddWithValue("@status", "Success");
            payCmd.ExecuteNonQuery();

            return RedirectToAction("Success");
            connection.Close();
        }
    }

    public IActionResult Success()
    {
        return View();
    }
    
    // GET
    public IActionResult Index()
    {
        return View();
    }
}