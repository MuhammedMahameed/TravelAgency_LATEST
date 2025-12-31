using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using TravelAgency.Models;

namespace TravelAgency.Controllers;

public class AccountController : Controller
{
    private readonly string _connStr;

    public AccountController(IConfiguration configuration)
    {
        _connStr = configuration.GetConnectionString("DefaultConnection");
    }

    [HttpGet]
    public IActionResult Register()
    {
        return View(new User());
    }

    [HttpPost]
    public IActionResult Register(User user)
    {
        if (!ModelState.IsValid)
        {
            return View(user);
        }

        using (SqlConnection connection = new SqlConnection(_connStr))
        {
            connection.Open();
            var cmd = new SqlCommand(
                @"INSERT INTO Users(FullName,Email,PasswordHash,Role,Status) 
                  VALUES(@name,@email,@pass, 'User', 'Active')", connection);

            cmd.Parameters.AddWithValue("@name", user.FullName);
            cmd.Parameters.AddWithValue("@email", user.Email);
            cmd.Parameters.AddWithValue("@pass", PasswordHelper.Hash(user.Password));

            using (var transaction = connection.BeginTransaction())
            {
                var isExsistCMD = new SqlCommand(
                @"SELECT COUNT(*) FROM Users WHERE Email = @email", connection,transaction);
                isExsistCMD.Parameters.AddWithValue("@email", user.Email);
                int count = (int)isExsistCMD.ExecuteScalar();
                if (count > 0)
                {
                    transaction.Rollback();
                    TempData["UserAlreadyExists"] =  "Email is already registered.";
                    return RedirectToAction("LogIn");
                }
            }

            cmd.ExecuteNonQuery();
            connection.Close();
        }

        return RedirectToAction("Login");
    }

    [HttpGet]
    public IActionResult Login()
    {
        return View();
    }

    [HttpPost]
    public IActionResult Login(string email, string password)
    {
        string role = "User";   
        string status = "Active";

        using (SqlConnection connection = new SqlConnection(_connStr))
        {
            connection.Open();
            var cmd = new SqlCommand(
                @"SELECT * FROM Users WHERE Email = @email AND PasswordHash = @pass", connection);
            cmd.Parameters.AddWithValue("@email", email);
            cmd.Parameters.AddWithValue("@pass", PasswordHelper.Hash(password));

            using (var reader = cmd.ExecuteReader())
            {
                if (!reader.Read())
                {
                    ViewBag.Error = "Username or password is incorrect";
                    return View();
                }

                status = reader["Status"]?.ToString() ?? "Active";
                role = reader["Role"]?.ToString() ?? "User";

                if (status == "Blocked")
                {
                    ViewBag.Error = "Your account is blocked. Please contact the admin.";
                    return View();
                }

                HttpContext.Session.SetInt32("UserId", (int)reader["UserId"]);
                HttpContext.Session.SetString("FullName", reader["FullName"]?.ToString() ?? "");
                HttpContext.Session.SetString("Role", role);
                HttpContext.Session.SetString("Status", status);
            }

            connection.Close();
        }

        if (role == "Admin")
        {
            return RedirectToAction("Index", "Admin");
        }

        return RedirectToAction("Gallery", "Trips");
    }

    public IActionResult Logout()
    {
        HttpContext.Session.Clear();
        return RedirectToAction("Login");
    }

    // GET
    public IActionResult Index()
    {
        return View();
    }
}
