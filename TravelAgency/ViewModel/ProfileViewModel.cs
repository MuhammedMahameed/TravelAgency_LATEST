namespace TravelAgency.ViewModel
{
    public class ProfileViewModel
    {
        public int UserId { get; set; }
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public string NationalId { get; set; } = "";
        public string Email { get; set; } = "";
        public string Role { get; set; } = "";
        public string Status { get; set; } = "";
        public string CreditCardNumber { get; set; } = "";
        public DateTime? CardExpiryDate { get; set; }
        public string? Cvc { get; set; }
    }
}
