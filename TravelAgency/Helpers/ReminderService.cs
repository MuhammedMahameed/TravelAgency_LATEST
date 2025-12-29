using Microsoft.Extensions.Hosting;
using Microsoft.Data.SqlClient;
using TravelAgency.Helpers;

namespace TravelAgency.Helpers
{
    public class ReminderService : BackgroundService
    {
        private readonly IConfiguration _config;

        public ReminderService(IConfiguration config)
        {
            _config = config;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                SendReminders();

                // ריצה פעם ב-24 שעות
                await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
            }
        }

        private void SendReminders()
        {
            using var conn = new SqlConnection(
                _config.GetConnectionString("DefaultConnection"));
            conn.Open();

            var cmd = new SqlCommand(@"
                SELECT u.Email, t.Destination, t.StartDate
                FROM Bookings b
                JOIN Trips t ON b.TripId = t.TripId
                JOIN Users u ON b.UserId = u.UserId
                WHERE b.Status = 'Active'
                AND CAST(t.StartDate AS DATE) =
                    CAST(DATEADD(day, 5, GETDATE()) AS DATE)
            ", conn);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                EmailHelper.Send(
                    reader["Email"].ToString(),
                    "Trip Reminder",
                    $"Reminder: Your trip to {reader["Destination"]} starts on {((DateTime)reader["StartDate"]).ToShortDateString()}."
                );
            }
        }
    }
}
