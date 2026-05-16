using System.ComponentModel.DataAnnotations;

namespace TravelAgency.Models;

public class User
{
    public int UserId { get; set; }

    [Required(ErrorMessage = "First name is required")]
    [StringLength(50, ErrorMessage = "First name must be at most 50 characters")]
    [Display(Name = "First Name")]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Last name is required")]
    [StringLength(50, ErrorMessage = "Last name must be at most 50 characters")]
    [Display(Name = "Last Name")]
    public string LastName { get; set; } = string.Empty;

    [Required(ErrorMessage = "National ID is required")]
    [StringLength(20, ErrorMessage = "National ID must be at most 20 characters")]
    [Display(Name = "National ID")]
    public string NationalId { get; set; } = string.Empty;

    [StringLength(256, ErrorMessage = "Email must be at most 256 characters")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required")]
    [DataType(DataType.Password)]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Password must be between 6 and 100 characters")]
    public string Password { get; set; } = string.Empty;

    public string Role { get; set; } = "User";

    public string Status { get; set; } = "Active";

    public string CreditCardNumber { get; set; } = string.Empty;

    [DataType(DataType.Date)]
    [Display(Name = "Card Expiry Date")]
    public DateTime? CardExpiryDate { get; set; }

    [StringLength(4, MinimumLength = 3, ErrorMessage = "CVC must be 3 or 4 digits")]
    [RegularExpression(@"^\d{3,4}$", ErrorMessage = "CVC must be 3 or 4 digits")]
    [Display(Name = "CVC")]
    public string? Cvc { get; set; }
}
