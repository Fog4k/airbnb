namespace AirbnbLite.Models
{
    public class Listing
    {
        public int Id { get; set; }
        public required string Title { get; set; }
        public required string Description { get; set; }
        public decimal PricePerNight { get; set; }
        public int OwnerId { get; set; }
        public string? PhotoUrl { get; set; }
    }
}