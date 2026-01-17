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
            try
            {
                using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
                conn.Open();

                var cmd = new SqlCommand(@"
            SELECT u.Email, t.PackageName, t.Destination, t.Country, t.StartDate, t.EndDate
            FROM Bookings b
            JOIN Trips t ON b.TripId = t.TripId
            JOIN Users u ON b.UserId = u.UserId
            WHERE b.Status = 'Active'
              AND CAST(t.StartDate AS DATE) = CAST(DATEADD(day, 5, GETDATE()) AS DATE)
        ", conn);

                using var reader = cmd.ExecuteReader();
                int count = 0;

                while (reader.Read())
                {
                    count++;

                    var email = reader["Email"]?.ToString() ?? "";
                    var packageName = reader["PackageName"] == DBNull.Value ? "" : reader["PackageName"]?.ToString() ?? "";
                    var destination = reader["Destination"]?.ToString() ?? "";
                    var country = reader["Country"]?.ToString() ?? "";
                    var startDate = (DateTime)reader["StartDate"];
                    var endDate = (DateTime)reader["EndDate"];

                    var title = !string.IsNullOrWhiteSpace(packageName) ? packageName : $"{destination}, {country}";

                    var body =
                        $"Friendly reminder: your upcoming trip is starting soon.\n\n" +
                        $"Trip: {title}\n" +
                        $"Dates: {startDate:dd/MM/yyyy} - {endDate:dd/MM/yyyy}\n\n" +
                        $"Travel Agency";

                    EmailHelper.Send(email, "Trip reminder – starts in 5 days", body);
                }

                Console.WriteLine($"[ReminderService] Sent reminders: {count}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ReminderService] ERROR: {ex.Message}");
            }
        }
    }
}
