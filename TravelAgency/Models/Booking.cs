using System;
using System.ComponentModel.DataAnnotations;
namespace TravelAgency.Models;

public class Booking
{
    public int BookingId { get; set; }

    [Required]
    public int UserId { get; set; }

    [Required]
    public int TripId { get; set; }

    [Required]
    [DataType(DataType.Date)]
    public DateTime BookingDate { get; set; }

    [StringLength(50)]
    public string Status { get; set; } = "Active";

    public bool IsPaid { get; set; }

    [Range(1, 500, ErrorMessage = "Quantity must be between 1 and 500")]
    public int Quantity { get; set; } = 1;

    [Range(0, 120, ErrorMessage = "Group minimum age must be between 0 and 120")]
    public int? GroupMinAge { get; set; }
}