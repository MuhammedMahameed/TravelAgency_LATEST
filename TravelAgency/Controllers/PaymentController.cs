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
                    @"SELECT t.Price 
                  FROM Bookings b 
                  JOIN Trips t ON b.TripId = t.TripId 
                  WHERE b.BookingId = @bid",
                    connection);

            priceCmd.Parameters.AddWithValue("@bid", bookingId);
            var priceObj = priceCmd.ExecuteScalar();
            if (priceObj == null)
                return Content("Invalid Booking.");

            decimal amount = (decimal)priceObj;

            var payCmd =
                new SqlCommand(
                    @"INSERT INTO Payments (BookingId, Amount, Status)
                  VALUES (@bid, @amount, @status)",
                    connection);

            payCmd.Parameters.AddWithValue("@bid", bookingId);
            payCmd.Parameters.AddWithValue("@amount", amount);
            payCmd.Parameters.AddWithValue("@status", "Success");
            payCmd.ExecuteNonQuery();

            try
            {
                var emailCmd = new SqlCommand(@"
                SELECT u.Email
                FROM Users u
                JOIN Bookings b ON u.UserId = b.UserId
                WHERE b.BookingId = @bid", connection);

                emailCmd.Parameters.AddWithValue("@bid", bookingId);

                var userEmail = emailCmd.ExecuteScalar()?.ToString();

                if (!string.IsNullOrEmpty(userEmail))
                {
                    EmailHelper.Send(
                        userEmail,
                        "Payment Confirmation",
                        "Your payment was successful. Thank you for booking with Travel Agency!"
                    );
                }
            }
            catch
            {
                // לא להפיל תשלום בגלל מייל
            }

            connection.Close();
            return RedirectToAction("Success");
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