using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Stripe;
using Stripe.Checkout;
using TravelAgency.Helpers;

namespace TravelAgency.Controllers;

public class PaymentController : Controller
{
    private readonly string _connStr;
    private readonly IConfiguration _config;

    public PaymentController(IConfiguration config)
    {
        _config = config;
        _connStr = config.GetConnectionString("DefaultConnection");
    }

    public IActionResult Pay(int bookingId)
    {
        if (!AuthHelper.IsLoggedIn(HttpContext))
            return RedirectToAction("Login", "Account");

        ViewBag.BookingId = bookingId;
        ViewBag.StripePublishableKey = _config["Stripe:PublishableKey"];
        return View();
    }

    // Creates a Stripe PaymentIntent for this booking and returns its client_secret.
    // Payment is confirmed client-side using Stripe Elements.
    [HttpPost]
    public IActionResult CreateIntent(int bookingId)
    {
        if (!AuthHelper.IsLoggedIn(HttpContext))
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(StripeConfiguration.ApiKey))
            return BadRequest(new { error = "Stripe is not configured. Set Stripe:SecretKey in appsettings.json" });

        int userId = HttpContext.Session.GetInt32("UserId")!.Value;

        using var conn = new SqlConnection(_connStr);
        conn.Open();

        // Ensure booking belongs to user, is active, and not already paid
        var infoCmd = new SqlCommand(@"
            SELECT t.Price, ISNULL(b.Quantity,1) AS Quantity, b.IsPaid
            FROM Bookings b
            JOIN Trips t ON b.TripId = t.TripId
            WHERE b.BookingId = @bid AND b.UserId = @uid AND b.Status = 'Active';", conn);
        infoCmd.Parameters.AddWithValue("@bid", bookingId);
        infoCmd.Parameters.AddWithValue("@uid", userId);

        decimal price;
        int qty;
        bool isPaid;

        using (var r = infoCmd.ExecuteReader())
        {
            if (!r.Read())
                return NotFound(new { error = "Booking not found." });

            price = (decimal)r["Price"];
            qty = Convert.ToInt32(r["Quantity"]);
            isPaid = (bool)r["IsPaid"];
        }

        if (isPaid)
            return BadRequest(new { error = "This booking is already paid." });

        if (price <= 0m)
            return BadRequest(new { error = "Trip price is invalid (must be > 0)." });

        if (qty < 1)
            return BadRequest(new { error = "Quantity is invalid (must be >= 1)." });

        // Stripe uses the smallest currency unit. Using ILS by default.
        // If you use a different currency, change this.
        var amount = (long)Math.Round(price * qty * 100m, MidpointRounding.AwayFromZero);
        if (amount < 50) // example minimum for ILS; use Stripe docs for exact
            return BadRequest(new { error = "Amount is below Stripe minimum for ILS." });

        var service = new PaymentIntentService();
        var intent = service.Create(new PaymentIntentCreateOptions
        {
            Amount = amount,
            Currency = "ils",
            AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions
            {
                Enabled = true
            },
            Metadata = new Dictionary<string, string>
            {
                ["bookingId"] = bookingId.ToString(),
                ["userId"] = userId.ToString()
            }
        });

        return Json(new { clientSecret = intent.ClientSecret });
    }

    // Called after client-side confirmation succeeds, so we can mark booking paid.
    [HttpPost]
    public IActionResult Confirm(int bookingId, string paymentIntentId)
    {
        if (!AuthHelper.IsLoggedIn(HttpContext))
            return Unauthorized();

        int userId = HttpContext.Session.GetInt32("UserId")!.Value;

        if (string.IsNullOrWhiteSpace(paymentIntentId))
            return BadRequest(new { error = "Missing paymentIntentId" });

        if (string.IsNullOrWhiteSpace(StripeConfiguration.ApiKey))
            return BadRequest(new { error = "Stripe is not configured." });

        // Verify intent status with Stripe
        var piService = new PaymentIntentService();
        var pi = piService.Get(paymentIntentId);

        if (!string.Equals(pi.Status, "succeeded", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Payment not completed." });

        using var conn = new SqlConnection(_connStr);
        conn.Open();

        using var tx = conn.BeginTransaction();
        try
        {
            // double-check booking ownership and not already paid
            var infoCmd = new SqlCommand(@"
                SELECT t.Price, ISNULL(b.Quantity,1) AS Quantity, b.IsPaid
                FROM Bookings b
                JOIN Trips t ON b.TripId = t.TripId
                WHERE b.BookingId = @bid AND b.UserId = @uid AND b.Status='Active';", conn, tx);
            infoCmd.Parameters.AddWithValue("@bid", bookingId);
            infoCmd.Parameters.AddWithValue("@uid", userId);

            decimal price;
            int qty;
            bool isPaid;

            using (var r = infoCmd.ExecuteReader())
            {
                if (!r.Read())
                {
                    tx.Rollback();
                    return NotFound(new { error = "Booking not found." });
                }

                price = (decimal)r["Price"];
                qty = Convert.ToInt32(r["Quantity"]);
                isPaid = (bool)r["IsPaid"];
            }

            if (isPaid)
            {
                tx.Rollback();
                return Ok(new { success = true });
            }

            decimal amount = price * qty;

            // Record payment
            var payCmd = new SqlCommand(@"
                INSERT INTO Payments (BookingId, Amount, Status)
                VALUES (@bid, @amount, 'Success');", conn, tx);
            payCmd.Parameters.AddWithValue("@bid", bookingId);
            payCmd.Parameters.AddWithValue("@amount", amount);
            payCmd.ExecuteNonQuery();

            // Mark booking paid
            var markCmd = new SqlCommand(@"
                UPDATE Bookings
                SET IsPaid = 1, PaidAt = SYSUTCDATETIME()
                WHERE BookingId = @bid AND UserId=@uid;", conn, tx);
            markCmd.Parameters.AddWithValue("@bid", bookingId);
            markCmd.Parameters.AddWithValue("@uid", userId);
            markCmd.ExecuteNonQuery();

            tx.Commit();
        }
        catch
        {
            try { tx.Rollback(); } catch { }
            return StatusCode(500, new { error = "Failed to finalize payment." });
        }

        // email on success (best-effort)
        try
        {
            var emailCmd = new SqlCommand(@"
                SELECT u.Email
                FROM Users u
                JOIN Bookings b ON u.UserId = b.UserId
                WHERE b.BookingId = @bid", conn);
            emailCmd.Parameters.AddWithValue("@bid", bookingId);
            var userEmail = emailCmd.ExecuteScalar()?.ToString();
            if (!string.IsNullOrEmpty(userEmail))
            {
                EmailHelper.Send(userEmail, "Payment Confirmation", "Your payment was successful. Thank you for booking with Travel Agency!");
            }
        }
        catch { }

        return Ok(new { success = true });
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
