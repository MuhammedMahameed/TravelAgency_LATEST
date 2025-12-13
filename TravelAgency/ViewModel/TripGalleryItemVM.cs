using TravelAgency.Models;
namespace TravelAgency.ViewModel;

public class TripGalleryItemVM
{
    public Trip Trip { get; set; } = new Trip();
    
    public int WaitingCount { get; set; }
    public int? MyPosition {get; set;}
    public bool IAmInWaitingList => MyPosition.HasValue;
    public bool IsMyTurn => MyPosition == 1;
    public string? ImagePath { get; set; }
}