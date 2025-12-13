using Microsoft.AspNetCore.Http;

namespace TravelAgency.Helpers
{
    public static class AuthHelper
    {
        public static bool IsLoggedIn(HttpContext context)
        {
            return context.Session.GetInt32("UserId") != null;
        }

        public static bool IsAdmin(HttpContext context)
        {
            return context.Session.GetString("Role") == "Admin";
        }
    }
}