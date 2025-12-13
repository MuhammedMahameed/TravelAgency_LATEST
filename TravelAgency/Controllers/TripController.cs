using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using TravelAgency.Models;

namespace TravelAgency.Controllers;

public class TripController : Controller
{
    private readonly string _conn;

    public TripController(IConfiguration config)
    {
        _conn = config.GetConnectionString("DefaultConnection");
    }
    // GET
    public IActionResult Index()
    {
        var trips = new List<Trip>();
        using (SqlConnection conn = new SqlConnection(_conn))
        {
            conn.Open();
            var sqlCommand = new SqlCommand("SELECT * FROM trips", conn);
            var reader = sqlCommand.ExecuteReader();
            while (reader.Read())
            {
                trips.Add(new Trip
                {
                    TripId = (int)reader["TripId"],
                    Destination = reader["Destination"].ToString(),
                    Country = reader["Country"].ToString(),
                    Price = (decimal)reader["Price"],
                    AvailableRooms =  (int)reader["AvailableRooms"]
                });
            }
            conn.Close();
        }
        return View(trips);
    }
}