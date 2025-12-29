using System;
using System.ComponentModel.DataAnnotations;

namespace TravelAgency.Models
{
    public class Trip
    {
        public int TripId { get; set; }

        [Required] public string Destination { get; set; } = string.Empty;

        [Required] public string Country { get; set; } = string.Empty;

        [Required]
        public DateTime StartDate { get; set; }
        [Required]
        public DateTime EndDate { get; set; }

        [Range(0, 100000)]
        public decimal Price { get; set; }

        //  Added for discounts
        public decimal? OldPrice { get; set; }
        public DateTime? DiscountEndDate { get; set; }

        [Range(0, 500)]
        public int AvailableRooms { get; set; }

        public string Category { get; set; } = string.Empty;

        public int? MinAge { get; set; }
        public string Description { get; set; } = string.Empty;

        public string? ImagePath { get; set; }
    }
}
