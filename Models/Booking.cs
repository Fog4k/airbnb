namespace AirbnbLite.Models
{
    public class Booking
    {
        public int Id { get; set; }
        public int ListingId { get; set; }
        public int UserId { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
    }
}