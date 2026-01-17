using System;
using System.ComponentModel.DataAnnotations;

namespace TravelAgency.Models
{
    public class Trip
    {
        public int TripId { get; set; }

        [Required(ErrorMessage = "Package name is required")]
        [StringLength(200, ErrorMessage = "Package name must be at most 200 characters")]
        public string PackageName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Destination is required")]
        [StringLength(150, ErrorMessage = "Destination must be at most 150 characters")]
        public string Destination { get; set; } = string.Empty;

        [Required(ErrorMessage = "Country is required")]
        [StringLength(100, ErrorMessage = "Country must be at most 100 characters")]
        public string Country { get; set; } = string.Empty;

        [Required(ErrorMessage = "Start date is required")]
        [DataType(DataType.Date)]
        public DateTime StartDate { get; set; }

        [Required(ErrorMessage = "End date is required")]
        [DataType(DataType.Date)]
        [DateGreaterThan("StartDate", ErrorMessage = "End date must be after start date")]
        public DateTime EndDate { get; set; }

        [Range(0, 100000, ErrorMessage = "Price must be between 0 and 100000")]
        public decimal Price { get; set; }

        [Range(0, 100000, ErrorMessage = "Old price must be between 0 and 100000")]
        public decimal? OldPrice { get; set; }

        public DateTime? DiscountEndDate { get; set; }

        [Range(0, 500, ErrorMessage = "Available rooms must be between 0 and 500")]
        public int AvailableRooms { get; set; }

        [Required(ErrorMessage = "Category is required")]
        [RegularExpression("^(Family|Honeymoon|Adventure|Cruise|Luxury)$",
            ErrorMessage = "Category must be one of: Family, Honeymoon, Adventure, Cruise, or Luxury.")]
        [StringLength(100, ErrorMessage = "Category must be at most 100 characters")]
        public string Category { get; set; } = string.Empty;

        [Range(0, 120, ErrorMessage = "Min age must be between 0 and 120")]
        public int? MinAge { get; set; }

        [StringLength(2000, ErrorMessage = "Description must be at most 2000 characters")]
        public string Description { get; set; } = string.Empty;

        public string? ImagePath { get; set; }

        [Range(0, 365, ErrorMessage = "Cancellation days must be between 0 and 365")]
        public int CancellationDays { get; set; } = 0;

        public bool IsHidden { get; set; } = false;
    }


}

public class DateGreaterThanAttribute : ValidationAttribute
{
    private readonly string _comparisonProperty;

    public DateGreaterThanAttribute(string comparisonProperty)
    {
        _comparisonProperty = comparisonProperty;
    }

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var currentValue = (DateTime?)value;

        var property = validationContext.ObjectType.GetProperty(_comparisonProperty);
        if (property == null) return new ValidationResult($"Unknown property: {_comparisonProperty}");

        var comparisonValue = (DateTime?)property.GetValue(validationContext.ObjectInstance);

        if (currentValue.HasValue && comparisonValue.HasValue && currentValue <= comparisonValue)
        {
            return new ValidationResult(ErrorMessage);
        }

        return ValidationResult.Success;
    }
}
