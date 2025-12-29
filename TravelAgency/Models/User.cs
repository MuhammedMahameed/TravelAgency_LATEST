using System.ComponentModel.DataAnnotations;
namespace TravelAgency.Models;

public class User
{
    public int UserId { get; set; }
    [Required] public string FullName { get; set; }
    [Required,EmailAddress] public string Email { get; set; }
    [Required] public string Password { get; set; }
    public string Role { get; set; } = "User";

    public string Status { get; set; } = "Active";

}