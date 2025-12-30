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
}