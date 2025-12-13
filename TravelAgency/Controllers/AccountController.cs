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
                @"INSERT INTO Users(FullName,Email,PasswordHash,Role) Values(@name,@email,@pass, 'User')", connection);
            
            cmd.Parameters.AddWithValue("@name", user.FullName);
            cmd.Parameters.AddWithValue("@email", user.Email);
            cmd.Parameters.AddWithValue("@pass", PasswordHelper.Hash(user.Password));

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
        using (SqlConnection connection = new SqlConnection(_connStr))
        {
            connection.Open();
            var cmd = new SqlCommand(
                @"SELECT * FROM Users WHERE Email = @email AND PasswordHash = @pass", connection);
            cmd.Parameters.AddWithValue("@email", email);
            cmd.Parameters.AddWithValue("@pass", PasswordHelper.Hash(password));

            var reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                ViewBag.Error = "Username or password is incorrect";
                return View();
            }
            HttpContext.Session.SetInt32("UserId", (int)reader["UserId"]);
            HttpContext.Session.SetString("FullName", reader["FullName"].ToString());
            HttpContext.Session.SetString("Role", reader["Role"].ToString());
            connection.Close();
        }
        return RedirectToAction("Index", "Home");
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