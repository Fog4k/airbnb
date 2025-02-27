using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AirbnbLite.Data;
using AirbnbLite.Models;
using System.Security.Claims;

namespace AirbnbLite.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ListingsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public ListingsController(AppDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> CreateListing([FromBody] Listing listing)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userIdClaim == null || !int.TryParse(userIdClaim, out int userId))
            {
                Console.WriteLine("Unauthorized: Invalid user ID in token");
                return Unauthorized("Invalid user ID in token");
            }

            listing.OwnerId = userId;
            listing.PhotoUrl = null; // PhotoUrl не обязателен при создании
            Console.WriteLine($"Creating listing: Title={listing.Title}, OwnerId={userId}, Price={listing.PricePerNight}");
            _context.Listings.Add(listing);
            try
            {
                await _context.SaveChangesAsync();
                Console.WriteLine($"Listing created with ID: {listing.Id}");
                return Ok(listing);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving listing: {ex.Message}");
                return StatusCode(500, $"Failed to create listing: {ex.Message}");
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetListings()
        {
            Console.WriteLine("Fetching all listings...");
            var listings = await _context.Listings.ToListAsync();
            Console.WriteLine($"Found {listings.Count} listings");
            return Ok(listings);
        }

        [HttpGet("search")]
        public async Task<IActionResult> SearchListings(
            [FromQuery] decimal? minPrice,
            [FromQuery] decimal? maxPrice,
            [FromQuery] string? title,
            [FromQuery] DateTime? startDate,
            [FromQuery] DateTime? endDate)
        {
            Console.WriteLine($"Searching listings with filters: minPrice={minPrice}, maxPrice={maxPrice}, title={title}, startDate={startDate}, endDate={endDate}");
            var query = _context.Listings.AsQueryable();

            if (minPrice.HasValue)
            {
                query = query.Where(l => l.PricePerNight >= minPrice.Value);
            }

            if (maxPrice.HasValue)
            {
                query = query.Where(l => l.PricePerNight <= maxPrice.Value);
            }

            if (!string.IsNullOrEmpty(title))
            {
                query = query.Where(l => l.Title.Contains(title));
            }

            if (startDate.HasValue && endDate.HasValue)
            {
                query = query.Where(l => !_context.Bookings
                    .Where(b => b.ListingId == l.Id
                        && b.StartDate < endDate.Value
                        && b.EndDate > startDate.Value)
                    .Any());
            }

            var listings = await query.ToListAsync();
            Console.WriteLine($"Search returned {listings.Count} listings");
            return Ok(listings);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetListing(int id)
        {
            Console.WriteLine($"Fetching listing ID: {id}");
            var listing = await _context.Listings.FindAsync(id);
            if (listing == null)
            {
                Console.WriteLine($"Listing ID: {id} not found");
                return NotFound();
            }
            return Ok(listing);
        }

        [HttpPut("{id}")]
        [Authorize]
        public async Task<IActionResult> UpdateListing(int id, [FromBody] Listing updatedListing)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userIdClaim == null || !int.TryParse(userIdClaim, out int userId))
            {
                Console.WriteLine("Unauthorized: Invalid user ID in token");
                return Unauthorized("Invalid user ID in token");
            }

            var listing = await _context.Listings.FindAsync(id);
            if (listing == null)
            {
                Console.WriteLine($"Listing ID: {id} not found");
                return NotFound();
            }

            // Разрешаем админам редактировать любые листинги
            var isAdmin = User.IsInRole("Admin");
            if (!isAdmin && listing.OwnerId != userId)
            {
                Console.WriteLine($"User {userId} not authorized to update listing ID: {id}");
                return Forbid();
            }

            listing.Title = updatedListing.Title;
            listing.Description = updatedListing.Description;
            listing.PricePerNight = updatedListing.PricePerNight;
            Console.WriteLine($"Updating listing ID: {id} with Title={listing.Title}, Price={listing.PricePerNight}");

            try
            {
                await _context.SaveChangesAsync();
                Console.WriteLine($"Listing ID: {id} updated");
                return Ok(listing);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating listing: {ex.Message}");
                return StatusCode(500, $"Failed to update listing: {ex.Message}");
            }
        }

        [HttpDelete("{id}")]
        [Authorize]
        public async Task<IActionResult> DeleteListing(int id)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userIdClaim == null || !int.TryParse(userIdClaim, out int userId))
            {
                Console.WriteLine("Unauthorized: Invalid user ID in token");
                return Unauthorized("Invalid user ID in token");
            }

            var listing = await _context.Listings.FindAsync(id);
            if (listing == null)
            {
                Console.WriteLine($"Listing ID: {id} not found");
                return NotFound();
            }

            // Разрешаем админам удалять любые листинги
            var isAdmin = User.IsInRole("Admin");
            if (!isAdmin && listing.OwnerId != userId)
            {
                Console.WriteLine($"User {userId} not authorized to delete listing ID: {id}");
                return Forbid();
            }

            _context.Listings.Remove(listing);
            try
            {
                await _context.SaveChangesAsync();
                Console.WriteLine($"Listing ID: {id} deleted");
                return NoContent();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deleting listing: {ex.Message}");
                return StatusCode(500, $"Failed to delete listing: {ex.Message}");
            }
        }

        [HttpGet("all")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetAllListings()
        {
            Console.WriteLine("Fetching all listings for admin...");
            var listings = await _context.Listings
                .Select(l => new
                {
                    l.Id,
                    l.Title,
                    l.Description,
                    l.PricePerNight,
                    l.OwnerId,
                    OwnerEmail = _context.Users
                        .Where(u => u.Id == l.OwnerId)
                        .Select(u => u.Email)
                        .FirstOrDefault() ?? "Unknown User"
                })
                .ToListAsync();
            Console.WriteLine($"Found {listings.Count} listings for admin");
            return Ok(listings);
        }

        [HttpGet("{id}/bookings")]
        [Authorize]
        public async Task<IActionResult> GetListingBookings(int id)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userIdClaim == null || !int.TryParse(userIdClaim, out int userId))
            {
                Console.WriteLine("Unauthorized: Invalid user ID in token");
                return Unauthorized("Invalid user ID in token");
            }

            var listing = await _context.Listings.FindAsync(id);
            if (listing == null)
            {
                Console.WriteLine($"Listing ID: {id} not found");
                return NotFound("Listing not found");
            }

            if (listing.OwnerId != userId)
            {
                Console.WriteLine($"User {userId} not authorized to view bookings for listing ID: {id}");
                return Forbid();
            }

            var bookings = await _context.Bookings
                .Where(b => b.ListingId == id)
                .ToListAsync();
            Console.WriteLine($"Found {bookings.Count} bookings for listing ID: {id}");
            return Ok(bookings);
        }

        [HttpPost("{id}/upload-photo")]
        [Authorize]
        public async Task<IActionResult> UploadPhoto(int id, IFormFile photo)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userIdClaim == null || !int.TryParse(userIdClaim, out int userId))
            {
                Console.WriteLine("Unauthorized: Invalid user ID in token");
                return Unauthorized("Invalid user ID in token");
            }

            var listing = await _context.Listings.FindAsync(id);
            if (listing == null)
            {
                Console.WriteLine($"Listing ID: {id} not found");
                return NotFound();
            }

            if (listing.OwnerId != userId)
            {
                Console.WriteLine($"User {userId} not authorized to upload photo for listing ID: {id}");
                return Forbid();
            }

            if (photo != null && photo.Length > 0)
            {
                var fileName = $"{id}_{Guid.NewGuid()}.jpg";
                var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/uploads/listings", fileName);
                Console.WriteLine($"Saving photo to: {filePath}");
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await photo.CopyToAsync(stream);
                }
                listing.PhotoUrl = $"/uploads/listings/{fileName}";
                Console.WriteLine($"Photo URL set to: {listing.PhotoUrl}");
                try
                {
                    await _context.SaveChangesAsync();
                    Console.WriteLine($"Photo saved for listing ID: {id}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error saving photo: {ex.Message}");
                    return StatusCode(500, $"Failed to save photo: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine("No photo provided for upload");
            }

            return Ok(listing);
        }
    }
}