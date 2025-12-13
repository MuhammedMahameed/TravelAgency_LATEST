using System;
namespace TravelAgency.Models;

public class Booking
{
    public int BookingId { get; set; }
    public int UserId { get; set; }
    public int TripId { get; set; }
    public DateTime BookingDate { get; set; }
    public string Status { get; set; } = "Active";
}