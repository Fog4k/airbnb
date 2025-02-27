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
    public class BookingsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public BookingsController(AppDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> CreateBooking([FromBody] Booking booking)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
            {
                return Unauthorized("Invalid user ID in token");
            }

            var listing = await _context.Listings.FindAsync(booking.ListingId);
            if (listing == null)
            {
                return BadRequest("Listing does not exist");
            }

            if (listing.OwnerId == userId)
            {
                return BadRequest("You cannot book your own listing.");
            }

            var conflictingBookings = await _context.Bookings
                .Where(b => b.ListingId == booking.ListingId
                    && b.StartDate < booking.EndDate
                    && b.EndDate > booking.StartDate)
                .AnyAsync();

            if (conflictingBookings)
            {
                return BadRequest("The requested dates are not available");
            }

            booking.UserId = userId;
            _context.Bookings.Add(booking);
            await _context.SaveChangesAsync();
            return Ok(booking);
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetBookings()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
            {
                return Unauthorized("Invalid user ID in token");
            }

            var bookings = await _context.Bookings
                .Where(b => b.UserId == userId)
                .Select(b => new
                {
                    b.Id,
                    b.ListingId,
                    ListingTitle = _context.Listings
                        .Where(l => l.Id == b.ListingId)
                        .Select(l => l.Title)
                        .FirstOrDefault() ?? "Unknown Listing",
                    b.StartDate,
                    b.EndDate
                })
                .ToListAsync();
            return Ok(bookings);
        }

        [HttpPut("{id}")]
        [Authorize]
        public async Task<IActionResult> UpdateBooking(int id, [FromBody] Booking booking)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
                return Unauthorized("Invalid user ID in token");

            var existingBooking = await _context.Bookings.FindAsync(id);
            if (existingBooking == null)
                return NotFound("Booking not found");

            if (existingBooking.UserId != userId)
                return Forbid("You can only edit your own bookings");

            if (booking.StartDate >= booking.EndDate)
                return BadRequest("StartDate must be earlier than EndDate");

            var conflictingBookings = await _context.Bookings
                .AnyAsync(b => b.ListingId == existingBooking.ListingId && b.Id != id &&
                               b.StartDate < booking.EndDate && b.EndDate > booking.StartDate);
            if (conflictingBookings)
                return BadRequest("The requested dates are not available");

            existingBooking.StartDate = booking.StartDate;
            existingBooking.EndDate = booking.EndDate;
            await _context.SaveChangesAsync();
            return Ok(existingBooking);
        }

        [HttpDelete("{id}")]
        [Authorize]
        public async Task<IActionResult> CancelBooking(int id)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
                return Unauthorized("Invalid user ID in token");

            var booking = await _context.Bookings.FindAsync(id);
            if (booking == null)
                return NotFound("Booking not found");

            if (booking.UserId != userId)
                return Forbid("You can only cancel your own bookings");

            _context.Bookings.Remove(booking);
            await _context.SaveChangesAsync();
            return NoContent();
        }

        [HttpGet("all")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetAllBookings()
        {
            var bookings = await _context.Bookings
                .Select(b => new
                {
                    b.Id,
                    b.ListingId,
                    ListingTitle = _context.Listings
                        .Where(l => l.Id == b.ListingId)
                        .Select(l => l.Title)
                        .FirstOrDefault() ?? "Unknown Listing",
                    b.UserId,
                    UserEmail = _context.Users
                        .Where(u => u.Id == b.UserId)
                        .Select(u => u.Email)
                        .FirstOrDefault() ?? "Unknown User",
                    b.StartDate,
                    b.EndDate
                })
                .ToListAsync();
            return Ok(bookings);
        }

        [HttpPut("admin/{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> AdminUpdateBooking(int id, [FromBody] Booking booking)
        {
            var existingBooking = await _context.Bookings.FindAsync(id);
            if (existingBooking == null)
                return NotFound("Booking not found");

            if (booking.StartDate >= booking.EndDate)
                return BadRequest("StartDate must be earlier than EndDate");

            var conflictingBookings = await _context.Bookings
                .AnyAsync(b => b.ListingId == existingBooking.ListingId && b.Id != id &&
                               b.StartDate < booking.EndDate && b.EndDate > booking.StartDate);
            if (conflictingBookings)
                return BadRequest("The requested dates are not available");

            existingBooking.StartDate = booking.StartDate;
            existingBooking.EndDate = booking.EndDate;
            await _context.SaveChangesAsync();
            return Ok(existingBooking);
        }

        [HttpDelete("admin/{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> AdminCancelBooking(int id)
        {
            var booking = await _context.Bookings.FindAsync(id);
            if (booking == null)
                return NotFound("Booking not found");

            _context.Bookings.Remove(booking);
            await _context.SaveChangesAsync();
            return NoContent();
        }
    }
}