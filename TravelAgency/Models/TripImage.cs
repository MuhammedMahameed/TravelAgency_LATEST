namespace TravelAgency.Models;

public class TripImage
{
    public int ImageId { get; set; }
    public int TripId { get; set; }
    public string ImagePath { get; set; } = "";
}
