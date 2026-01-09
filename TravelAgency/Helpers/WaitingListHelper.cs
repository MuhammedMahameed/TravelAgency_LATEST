using Microsoft.Data.SqlClient;
using System;
using TravelAgency.Helpers;

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
                // נבדוק אם יש מישהו בתור עם ExpirationAt בתוקף
                var checkActive = new SqlCommand(@"
                    SELECT COUNT(*) FROM WaitingList
                    WHERE TripId=@tid AND ExpirationAt IS NOT NULL AND GETDATE() < ExpirationAt", conn);
                checkActive.Parameters.AddWithValue("@tid", tripId);
                int activeCount = (int)checkActive.ExecuteScalar();

                // אם כבר יש מישהו עם תוקף פעיל – לא שולחים שוב
                if (activeCount > 0)
                    return;

                // נמצא את הבא בתור שעדיין לא קיבל הודעה
                var nextCmd = new SqlCommand(@"
                    SELECT TOP 1 w.WaitingId, u.Email
                    FROM WaitingList w
                    JOIN Users u ON w.UserId = u.UserId
                    WHERE w.TripId=@tid AND w.NotifiedAt IS NULL
                    ORDER BY w.JoinDate", conn);
                nextCmd.Parameters.AddWithValue("@tid", tripId);

                using var r = nextCmd.ExecuteReader();
                if (r.Read())
                {
                    int waitingId = Convert.ToInt32(r["WaitingId"]);
                    string email = r["Email"].ToString();
                    r.Close();

                    // נעדכן תוקף 24 שעות + זמן הודעה
                    var update = new SqlCommand(@"
                        UPDATE WaitingList
                        SET NotifiedAt=GETDATE(), ExpirationAt=DATEADD(hour,24,GETDATE())
                        WHERE WaitingId=@id", conn);
                    update.Parameters.AddWithValue("@id", waitingId);
                    update.ExecuteNonQuery();

                    // שליחת מייל
                    EmailHelper.Send(
                        email,
                        "Room available for your trip!",
                        "A room has just become available for the trip you wanted. You have 24 hours to complete your booking before the opportunity moves to the next person."
                    );
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
