using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using TravelAgency.Helpers;
using TravelAgency.ViewModel;
namespace TravelAgency.Controllers;

public class BookingController : Controller
{
    private readonly string _connStr;
    public BookingController(IConfiguration config)
    {
        _connStr = config.GetConnectionString("DefaultConnection");
    }

    public IActionResult Start(int id)
    {
        if (!AuthHelper.IsLoggedIn(HttpContext))
        {
            return RedirectToAction("Login", "Account");
        }
        int userId = HttpContext.Session.GetInt32("UserId").Value;
        using (SqlConnection connection = new SqlConnection(_connStr))
        {
            connection.Open();
            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    var countCmd =
                        new SqlCommand(@"SELECT COUNT(*) FROM Bookings WHERE UserId = @uid AND Status =@status",
                            connection, transaction);
                    countCmd.Parameters.AddWithValue("@uid", userId);
                    countCmd.Parameters.AddWithValue("@status", "Active");

                    int activeBookings = (int)countCmd.ExecuteScalar();
                    if (activeBookings >= 3)
                    {
                        transaction.Rollback();
                        return Content("You cannot book more than 3 active trips.");
                    }
                    var waitCmd = new SqlCommand(
                        @"SELECT TOP 1 UserId FROM WaitingList "+
                        " WHERE TripId=@tid"+
                        " ORDER BY JoinDate", connection, transaction);

                    waitCmd.Parameters.AddWithValue("@tid", id);

                    var first = waitCmd.ExecuteScalar();

                    if (first != null && (int)first != userId)
                    {
                        transaction.Rollback();
                        return Content("You are not first in the waiting list.");
                    }

                    var roomCmd = new SqlCommand(@"SELECT AvailableRooms FROM Trips WITH (UPDLOCK) WHERE TripId = @tid",
                        connection, transaction);
                    roomCmd.Parameters.AddWithValue("@tid", id);

                    var roomsObj = roomCmd.ExecuteScalar();
                    if (roomsObj == null)
                    {
                        transaction.Rollback();
                        return Content("Trip not found.");
                    }

                    int rooms = (int)roomsObj;
                    if (rooms <= 0)
                    {
                        transaction.Rollback();
                        return RedirectToAction("Join", "WaitingList", new { tripId = id });
                    }
                    
                    var bookCmd = new SqlCommand(
                        @"INSERT INTO Bookings (UserId, TripId, Status) VALUES (@uid, @tid, @status)",
                        connection, transaction);

                    bookCmd.Parameters.AddWithValue("@uid", userId);
                    bookCmd.Parameters.AddWithValue("@tid", id);
                    bookCmd.Parameters.AddWithValue("@status", "Active");
                    bookCmd.ExecuteNonQuery();

                    var updateCmd = new SqlCommand(@"UPDATE Trips SET AvailableRooms = AvailableRooms - 1 " +
                                                   " WHERE TripId=@tid", connection, transaction);


                    updateCmd.Parameters.AddWithValue("@tid", id);
                    updateCmd.ExecuteNonQuery();
                    
                    var delCmd = new SqlCommand(
                        @"DELETE FROM WaitingList"+
                        " WHERE TripId=@tid AND UserId=@uid", connection, transaction);

                    delCmd.Parameters.AddWithValue("@tid", id);
                    delCmd.Parameters.AddWithValue("@uid", userId);
                    delCmd.ExecuteNonQuery();
                    
                    
                    transaction.Commit();
                    connection.Close();

                    return RedirectToAction("MyBookings");
                }
                // catch (Exception ex)
                // {
                //     transaction.Rollback();
                //     return Content(ex.Message);
                // }
                catch
                {
                    transaction.Rollback();
                    return Content("Booking failed. Please try again.");
                }
            }
        }
    }

    public IActionResult MyBookings()
    {
        if(!AuthHelper.IsLoggedIn(HttpContext))
            return RedirectToAction("Login", "Account");
        
        int userId = HttpContext.Session.GetInt32("UserId").Value;
        var list = new List<BookingViewModel>();
        using (SqlConnection connection = new SqlConnection(_connStr))
        {
            connection.Open();
            var cmd = new SqlCommand(
                @"SELECT b.BookingId, t.Destination, t.Country, t.StartDate, b.Status "+
              "FROM Bookings b " +
              " JOIN Trips t ON b.TripId = t.TripId " +
              " WHERE b.UserId=@uid AND b.Status=@status", connection);
            
            cmd.Parameters.AddWithValue("@uid", userId);
            cmd.Parameters.AddWithValue("@status", "Active");
            
            var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new BookingViewModel
                {
                    BookingId = (int)reader["BookingId"],
                    Destination = reader["Destination"].ToString(),
                    Country = reader["Country"].ToString(),
                    StartDate = (DateTime)reader["StartDate"],
                });
            }
            connection.Close();
            
            return View(list);

        }
    }

    [HttpPost]
    public IActionResult Cancel(int bookingId)
    {
        if (!AuthHelper.IsLoggedIn(HttpContext))
            return RedirectToAction("Login", "Account");

        int userId = HttpContext.Session.GetInt32("UserId").Value;

        using (SqlConnection connection = new SqlConnection(_connStr))
        {
            connection.Open();
            using var transaction = connection.BeginTransaction();
            try
            {
                var getCmd = new SqlCommand(
                    @"SELECT TripId FROM Bookings WHERE BookingId = @bid AND UserId = @uid AND Status =@status",
                    connection, transaction);

                getCmd.Parameters.AddWithValue("@bid", bookingId);
                getCmd.Parameters.AddWithValue("@uid", userId);
                getCmd.Parameters.AddWithValue("@status", "Active");
                var tripObj = getCmd.ExecuteScalar();
                if (tripObj == null)
                {
                    transaction.Rollback();
                    return Content("Booking not found (or already cancelled).");
                }

                int tripId = (int)tripObj;
                var cancelCmd = new SqlCommand(
                    @"UPDATE Bookings SET Status = @cancelled WHERE BookingId = @bid AND UserId = @uid and Status = @status",
                    connection, transaction);

                cancelCmd.Parameters.AddWithValue("@cancelled", "Cancelled");
                cancelCmd.Parameters.AddWithValue("@bid", bookingId);
                cancelCmd.Parameters.AddWithValue("@uid", userId);
                cancelCmd.Parameters.AddWithValue("@status", "Active");

                cancelCmd.ExecuteNonQuery();

                var roomCmd = new SqlCommand(
                    @"UPDATE Trips Set AvailableRooms = AvailableRooms + 1 WHERE TripId = @tid",
                    connection, transaction);
                roomCmd.Parameters.AddWithValue("@tid", tripId);
                roomCmd.ExecuteNonQuery();
                transaction.Commit();
                connection.Close();
                return RedirectToAction("MyBookings");
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return Content(ex.Message);
            }
        }
    }
    // GET
    public IActionResult Index()
    {
        return View();
    }
}