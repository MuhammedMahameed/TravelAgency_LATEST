namespace TravelAgency.ViewModel;

public class BookingViewModel
{
    public int BookingId { get; set; }
    public string Destination { get; set; }
    public string Country { get; set; }
    public DateTime StartDate { get; set; }

    public string Status { get; set; } = string.Empty;

}