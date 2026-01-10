namespace TravelAgency.ViewModel;

public class BookingViewModel
{
    public int BookingId { get; set; }
    public int TripId { get; set; }

    public string PackageName { get; set; } = string.Empty;

    public string Destination { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;

    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }

    public decimal Price { get; set; }
    public string? Category { get; set; }

    public int? TripMinAge { get; set; }        // Trips.MinAge
    public int? GroupMinAge { get; set; }       // Bookings.GroupMinAge
    public int CancellationDays { get; set; }   // Trips.CancellationDays

    public DateTime BookingDate { get; set; }   // Bookings.BookingDate
    public bool IsPaid { get; set; }
    public DateTime? PaidAt { get; set; }

    public string Status { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;

    public string? ImagePath { get; set; }
    public string? Description { get; set; }
}
