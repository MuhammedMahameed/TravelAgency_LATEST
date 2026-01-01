using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using TravelAgency.Helpers;

namespace TravelAgency.Controllers
{
    [Route("SiteReviews")]
    public class SiteReviewsController : Controller
    {
        private readonly string _connStr;

        public SiteReviewsController(IConfiguration config)
        {
            _connStr = config.GetConnectionString("DefaultConnection");
        }

        [HttpPost("Add")]
        public IActionResult Add(int rating, string comment)
        {
            if (!AuthHelper.IsLoggedIn(HttpContext))
                return Unauthorized();

            int userId = HttpContext.Session.GetInt32("UserId")!.Value;

            if (rating < 1 || rating > 5)
                return BadRequest();

            comment ??= "";
            if (comment.Length > 500) comment = comment.Substring(0, 500);

            using var conn = new SqlConnection(_connStr);
            conn.Open();

            var cmd = new SqlCommand(
                "INSERT INTO SiteReviews (UserId, Rating, Comment) OUTPUT INSERTED.ReviewId VALUES (@uid,@r,@c)",
                conn);
            cmd.Parameters.AddWithValue("@uid", userId);
            cmd.Parameters.AddWithValue("@r", rating);
            cmd.Parameters.AddWithValue("@c", string.IsNullOrWhiteSpace(comment) ? (object)DBNull.Value : comment);

            int id = (int)cmd.ExecuteScalar();

            return Ok(new { reviewId = id });
        }

        [HttpGet("List")]
        public IActionResult List()
        {
            var list = new List<object>();

            using var conn = new SqlConnection(_connStr);
            conn.Open();

            var cmd = new SqlCommand(@"
                SELECT r.ReviewId, r.UserId, r.Rating, r.Comment, r.CreatedAt, u.FullName
                FROM SiteReviews r 
                JOIN Users u ON r.UserId = u.UserId
                ORDER BY r.CreatedAt DESC", conn);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new
                {
                    reviewId = (int)r["ReviewId"],
                    userId = (int)r["UserId"],
                    rating = (int)r["Rating"],
                    comment = r["Comment"] == DBNull.Value ? "" : r["Comment"].ToString(),
                    createdAt = ((DateTime)r["CreatedAt"]).ToString("dd/MM/yyyy"),
                    fullName = r["FullName"].ToString()
                });
            }

            return Json(list);
        }

        // ✅ מחיקה: Admin מוחק הכל, משתמש מוחק רק את שלו
        [HttpPost("Delete")]
        public IActionResult Delete(int id)
        {
            if (!AuthHelper.IsLoggedIn(HttpContext))
                return Unauthorized();

            var role = HttpContext.Session.GetString("Role");
            int currentUserId = HttpContext.Session.GetInt32("UserId")!.Value;

            using var conn = new SqlConnection(_connStr);
            conn.Open();

            // מי כתב את הביקורת?
            var ownerCmd = new SqlCommand("SELECT UserId FROM SiteReviews WHERE ReviewId=@id", conn);
            ownerCmd.Parameters.AddWithValue("@id", id);
            var ownerObj = ownerCmd.ExecuteScalar();

            if (ownerObj == null || ownerObj == DBNull.Value)
                return NotFound();

            int ownerUserId = (int)ownerObj;

            // אם לא אדמין וגם לא הבעלים -> אין הרשאה
            if (role != "Admin" && ownerUserId != currentUserId)
                return Unauthorized();

            var delCmd = new SqlCommand("DELETE FROM SiteReviews WHERE ReviewId=@id", conn);
            delCmd.Parameters.AddWithValue("@id", id);
            delCmd.ExecuteNonQuery();

            return Ok(new { success = true });
        }
    }
}
