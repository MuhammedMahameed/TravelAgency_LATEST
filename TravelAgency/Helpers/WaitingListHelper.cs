using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;

namespace TravelAgency.Helpers
{
    public static class WaitingListHelper
    {
        public static void ProcessTripWaitingList(string connStr, int tripId)
        {
            using var conn = new SqlConnection(connStr);
            conn.Open();

            try
            {
                var roomsCmd = new SqlCommand("SELECT AvailableRooms FROM Trips WHERE TripId=@tid", conn);
                roomsCmd.Parameters.AddWithValue("@tid", tripId);
                var roomsObj = roomsCmd.ExecuteScalar();
                if (roomsObj == null || roomsObj == DBNull.Value)
                    return;

                int availableRooms = (int)roomsObj;
                if (availableRooms <= 0)
                    return;

                var activeNotifiedCmd = new SqlCommand(@"
                    SELECT COUNT(*) FROM WaitingList
                    WHERE TripId=@tid AND ExpirationAt IS NOT NULL AND GETDATE() < ExpirationAt", conn);
                activeNotifiedCmd.Parameters.AddWithValue("@tid", tripId);
                int activeNotified = (int)activeNotifiedCmd.ExecuteScalar();

                int freeSlots = availableRooms - activeNotified;
                if (freeSlots <= 0)
                    return;

                var nextCmd = new SqlCommand(@"
                    SELECT TOP (@n) w.WaitingId, u.Email
                    FROM WaitingList w
                    JOIN Users u ON w.UserId = u.UserId
                    WHERE w.TripId = @tid AND w.NotifiedAt IS NULL
                    ORDER BY w.JoinDate", conn);
                nextCmd.Parameters.AddWithValue("@tid", tripId);
                nextCmd.Parameters.AddWithValue("@n", freeSlots);

                var toNotify = new List<(int waitingId, string email)>();
                using (var r = nextCmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        toNotify.Add((Convert.ToInt32(r["WaitingId"]), r["Email"]?.ToString() ?? string.Empty));
                    }
                }

                string title = "";
                string destination = "";
                string country = "";
                DateTime? startDate = null;
                DateTime? endDate = null;

                var tripInfoCmd = new SqlCommand(@"
                    SELECT PackageName, Destination, Country, StartDate, EndDate
                    FROM Trips
                    WHERE TripId = @tid", conn);
                tripInfoCmd.Parameters.AddWithValue("@tid", tripId);
                using (var tr = tripInfoCmd.ExecuteReader())
                {
                    if (tr.Read())
                    {
                        title = (tr["PackageName"] == DBNull.Value ? "" : tr["PackageName"]?.ToString() ?? "").Trim();
                        destination = tr["Destination"]?.ToString() ?? "";
                        country = tr["Country"]?.ToString() ?? "";
                        startDate = tr["StartDate"] == DBNull.Value ? null : (DateTime?)tr["StartDate"];
                        endDate = tr["EndDate"] == DBNull.Value ? null : (DateTime?)tr["EndDate"];
                    }
                }

                var displayTitle = !string.IsNullOrWhiteSpace(title) ? title : $"{destination}, {country}";
                var dateRange = (startDate.HasValue && endDate.HasValue)
                    ? $"{startDate.Value:dd/MM/yyyy} - {endDate.Value:dd/MM/yyyy}"
                    : "(dates unavailable)";

                foreach (var entry in toNotify)
                {
                    var update = new SqlCommand(@"
                        UPDATE WaitingList
                        SET NotifiedAt = GETDATE(), ExpirationAt = DATEADD(hour,24,GETDATE())
                        WHERE WaitingId = @id", conn);
                    update.Parameters.AddWithValue("@id", entry.waitingId);
                    update.ExecuteNonQuery();

                    try
                    {
                        var body =
                            $"Good news! A room is now available for your trip.\n\n" +
                            $"Trip: {displayTitle}\n" +
                            $"Dates: {dateRange}\n\n" +
                            $"You have 24 hours to complete your booking before the offer moves to the next person in the waiting list.\n\n" +
                            $"Visit the site to book now: /Trips/Gallery\n\n" +
                            $"Travel Agency";

                        EmailHelper.Send(entry.email, "Room available – complete your booking", body);
                    }
                    catch { }
                }

                var remainingCmd = new SqlCommand(@"SELECT COUNT(*) FROM WaitingList WHERE TripId=@tid AND NotifiedAt IS NULL", conn);
                remainingCmd.Parameters.AddWithValue("@tid", tripId);
                int remaining = (int)remainingCmd.ExecuteScalar();

                if (remaining == 0)
                {
                    var delAll = new SqlCommand("DELETE FROM WaitingList WHERE TripId=@tid", conn);
                    delAll.Parameters.AddWithValue("@tid", tripId);
                    delAll.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error processing waiting list: " + ex.Message);
            }

            conn.Close();
        }
    }
}
