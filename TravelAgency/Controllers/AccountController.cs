using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using TravelAgency.Helpers;
using TravelAgency.Models;
using TravelAgency.ViewModel;

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
                @"SELECT COUNT(*) FROM Users WHERE Email = @email", connection, transaction);
                isExsistCMD.Parameters.AddWithValue("@email", user.Email);
                int count = (int)isExsistCMD.ExecuteScalar();
                if (count > 0)
                {
                    transaction.Rollback();
                    TempData["UserAlreadyExists"] = "Email is already registered.";
                    return RedirectToAction("Login");
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

    public IActionResult Index()
    {
        return View();
    }

    [HttpGet]
    [HttpGet]
    public IActionResult ForgotPassword()
    {
        TempData["Error"] = null;
        TempData["Success"] = null;
        return View();
    }

    [HttpPost]
    public IActionResult ForgotPassword(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            TempData["Error"] = "Please enter your email address.";
            return View();
        }

        using (var conn = new SqlConnection(_connStr))
        {
            conn.Open();
            var cmd = new SqlCommand("SELECT UserId FROM Users WHERE Email=@e", conn);
            cmd.Parameters.AddWithValue("@e", email);
            var userObj = cmd.ExecuteScalar();

            if (userObj == null)
            {
                TempData["Error"] = "Email not found in our system.";
                return View();
            }

            string resetToken = Guid.NewGuid().ToString();

            var createTableCmd = new SqlCommand(@"
                IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='PasswordResets' AND xtype='U')
                CREATE TABLE PasswordResets (
                    Id INT IDENTITY(1,1) PRIMARY KEY,
                    Email NVARCHAR(200) NOT NULL,
                    Token NVARCHAR(200) NOT NULL,
                    ExpireAt DATETIME NOT NULL
                )", conn);
            createTableCmd.ExecuteNonQuery();

            var tokenCmd = new SqlCommand(@"
                INSERT INTO PasswordResets (Email, Token, ExpireAt)
                VALUES (@e, @t, DATEADD(HOUR, 1, GETDATE()))", conn);
            tokenCmd.Parameters.AddWithValue("@e", email);
            tokenCmd.Parameters.AddWithValue("@t", resetToken);
            tokenCmd.ExecuteNonQuery();

            string resetLink = $"https://localhost:7217/Account/ChangePassword?token={resetToken}";

            EmailHelper.Send(
                email,
                "Travel Agency - Reset your password",
                $"Click the link below to reset your password:\n{resetLink}\n\nThis link will expire in 1 hour."
            );

            TempData["Success"] = "A password reset link has been sent to your email.";
            return RedirectToAction("Login");
        }
    }

    [HttpGet]
    public IActionResult ChangePassword(string? token)
    {

        TempData["Success"] = null;
        TempData["Error"] = null;
        if (!string.IsNullOrEmpty(token))
        {
            using (var conn = new SqlConnection(_connStr))
            {
                conn.Open();
                var cmd = new SqlCommand(
                    "SELECT Email FROM PasswordResets WHERE Token=@t AND ExpireAt > GETDATE()", conn);
                cmd.Parameters.AddWithValue("@t", token);
                var email = cmd.ExecuteScalar()?.ToString();

                if (email == null)
                {
                    TempData["Error"] = "Reset link is invalid or expired.";
                    return RedirectToAction("ForgotPassword");
                }

                HttpContext.Session.SetString("ResetEmail", email);
            }
        }
        else if (HttpContext.Session.GetString("ResetEmail") == null)
        {
            return RedirectToAction("ForgotPassword");
        }

        return View();
    }

    [HttpPost]
    public IActionResult ChangePassword(string newPassword, string confirmPassword)
    {
        string? email = HttpContext.Session.GetString("ResetEmail");
        if (email == null)
            return RedirectToAction("ForgotPassword");

        if (string.IsNullOrWhiteSpace(newPassword) || newPassword != confirmPassword)
        {
            TempData["Error"] = "Passwords do not match.";
            return View();
        }

        using (var conn = new SqlConnection(_connStr))
        {
            conn.Open();
            string hashed = PasswordHelper.Hash(newPassword);
            var cmd = new SqlCommand("UPDATE Users SET PasswordHash=@p WHERE Email=@e", conn);
            cmd.Parameters.AddWithValue("@p", hashed);
            cmd.Parameters.AddWithValue("@e", email);
            cmd.ExecuteNonQuery();

            var del = new SqlCommand("DELETE FROM PasswordResets WHERE Email=@e", conn);
            del.Parameters.AddWithValue("@e", email);
            del.ExecuteNonQuery();
        }

        HttpContext.Session.Remove("ResetEmail");
        TempData["Success"] = "Your password has been updated successfully.";
        return RedirectToAction("Login");
    }


    [HttpGet]
    public IActionResult Profile()
    {
        int? userId = HttpContext.Session.GetInt32("UserId");
        if (userId == null)
            return RedirectToAction("Login");

        var vm = new ProfileViewModel();

        using (var conn = new SqlConnection(_connStr))
        {
            conn.Open();
            var cmd = new SqlCommand(@"
            SELECT UserId, FullName, Email, Role, Status
            FROM Users
            WHERE UserId = @id", conn);

            cmd.Parameters.AddWithValue("@id", userId.Value);

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                vm.UserId = (int)reader["UserId"];
                vm.FullName = reader["FullName"]?.ToString() ?? "";
                vm.Email = reader["Email"]?.ToString() ?? "";
                vm.Role = reader["Role"]?.ToString() ?? "User";
                vm.Status = reader["Status"]?.ToString() ?? "Active";
            }
            else
            {
                HttpContext.Session.Clear();
                return RedirectToAction("Login");
            }
        }

        return View(vm);
    }

    [HttpPost]
    public IActionResult Profile(ProfileViewModel vm)
    {
        int? userId = HttpContext.Session.GetInt32("UserId");
        if (userId == null)
            return RedirectToAction("Login");

        if (string.IsNullOrWhiteSpace(vm.FullName))
        {
            TempData["Error"] = "Full name is required.";
            return View(vm);
        }

        using (var conn = new SqlConnection(_connStr))
        {
            conn.Open();
            var cmd = new SqlCommand(@"
            UPDATE Users
            SET FullName = @name
            WHERE UserId = @id", conn);

            cmd.Parameters.AddWithValue("@name", vm.FullName.Trim());
            cmd.Parameters.AddWithValue("@id", userId.Value);

            cmd.ExecuteNonQuery();
        }

        HttpContext.Session.SetString("FullName", vm.FullName.Trim());

        TempData["Success"] = "Profile updated successfully!";
        return RedirectToAction("Profile");
    }

}
