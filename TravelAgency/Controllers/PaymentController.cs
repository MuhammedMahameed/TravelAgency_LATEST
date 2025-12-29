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
   public IActionResult Process(int bookingId, string cardholder, string cardNumber, string expMonth, string expYear, string cvv)
    {
        if (!AuthHelper.IsLoggedIn(HttpContext))
            return RedirectToAction("Login", "Account");

        // sanitize inputs (only digits)
        cardNumber = new string((cardNumber ?? "").Where(char.IsDigit).ToArray());
        cvv = new string((cvv ?? "").Where(char.IsDigit).ToArray());

        bool validCard =
            !string.IsNullOrWhiteSpace(cardholder) &&
            cardNumber.Length >= 12 && cardNumber.Length <= 19 &&
            PassesLuhn(cardNumber) &&
            IsExpiryValid(expMonth, expYear) &&
            (cvv.Length == 3 || cvv.Length == 4);

        using (SqlConnection connection = new SqlConnection(_connStr))
        {
            connection.Open();

            // Make sure booking exists + get amount + check already paid
            var infoCmd = new SqlCommand(@"
                SELECT t.Price, b.IsPaid
                FROM Bookings b
                JOIN Trips t ON b.TripId = t.TripId
                WHERE b.BookingId = @bid", connection);

            infoCmd.Parameters.AddWithValue("@bid", bookingId);

            using var r = infoCmd.ExecuteReader();
            if (!r.Read())
            {
                TempData["PaymentMessage"] = "Payment failed: Invalid booking.";
                return RedirectToAction("Index", "Home");
            }

            decimal amount = (decimal)r["Price"];
            bool isPaid = (bool)r["IsPaid"];
            r.Close();

            if (isPaid)
            {
                TempData["PaymentMessage"] = "This booking is already paid.";
                return RedirectToAction("Index", "Home");
            }

            // Decide status
            bool success = validCard; // you can add random decline if you want
            string status = success ? "Success" : "Failed";

            // Insert payment (NO card number stored)
            var payCmd = new SqlCommand(@"
                INSERT INTO Payments (BookingId, Amount, Status)
                VALUES (@bid, @amount, @status)", connection);

            payCmd.Parameters.AddWithValue("@bid", bookingId);
            payCmd.Parameters.AddWithValue("@amount", amount);
            payCmd.Parameters.AddWithValue("@status", status);
            payCmd.ExecuteNonQuery();

            // Mark booking paid only if success
            if (success)
            {
                var markCmd = new SqlCommand(@"
                    UPDATE Bookings
                    SET IsPaid = 1, PaidAt = SYSUTCDATETIME()
                    WHERE BookingId = @bid", connection);

                markCmd.Parameters.AddWithValue("@bid", bookingId);
                markCmd.ExecuteNonQuery();
            }

            // Email only on success (optional)
            if (success)
            {
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
                catch { }
            }

            TempData["PaymentMessage"] = success
                ? "Payment successful!"
                : "Payment failed. Card details are invalid or declined.";

            return RedirectToAction("Index", "Home");
        }
    }

private static bool PassesLuhn(string digits)
{
    int sum = 0;
    bool alt = false;

    for (int i = digits.Length - 1; i >= 0; i--)
    {
        int n = digits[i] - '0';
        if (alt)
        {
            n *= 2;
            if (n > 9) n -= 9;
        }
        sum += n;
        alt = !alt;
    }

    return sum % 10 == 0;
}

private static bool IsExpiryValid(string mm, string yy)
{
    if (!int.TryParse(mm, out int m) || m < 1 || m > 12) return false;
    if (!int.TryParse(yy, out int y)) return false;

    // interpret YY as 20YY
    if (y < 100) y += 2000;

    var now = DateTime.UtcNow;
    var exp = new DateTime(y, m, 1).AddMonths(1); // valid until end of month
    return exp > now;
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