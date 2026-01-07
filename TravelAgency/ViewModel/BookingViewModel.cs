using System.ComponentModel.DataAnnotations;

namespace TravelAgency.ViewModel;

public class BookingViewModel
{     
    public int BookingId { get; set; }

    [Required]
    [StringLength(150)]
    public string Destination { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string Country { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Date)]
    public DateTime StartDate { get; set; }

    public string Status { get; set; } = string.Empty;
    public bool IsPaid { get; set; }

    public string? ImagePath { get; set; }

}