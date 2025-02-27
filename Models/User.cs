namespace AirbnbLite.Models
{
    public class User
    {
        public int Id { get; set; }
        public required string Email { get; set; }
        public required string PasswordHash { get; set; }
        public required string Name { get; set; }
        public required string Roles { get; set; }
        public string? AvatarUrl { get; set; }
    }
}