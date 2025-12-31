using System;
using System.ComponentModel.DataAnnotations;

namespace TravelAgency.Models
{
    public class Review
    {
        public int ReviewId { get; set; }

        [Required]
        public int TripId { get; set; }

        [Required]
        public int UserId { get; set; }

        [Range(1, 5, ErrorMessage = "Rating must be between 1 and 5")]
        public int Rating { get; set; }

        [StringLength(500)]
        public string? Comment { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
