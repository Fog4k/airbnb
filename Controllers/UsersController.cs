using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AirbnbLite.Data;
using AirbnbLite.Models;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using System.Net.Mail;
using System.Net;
using System.Collections.Generic;

namespace AirbnbLite.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UsersController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _config;
        private static readonly Dictionary<string, string> _resetCodes = new Dictionary<string, string>();

        public UsersController(AppDbContext context, IConfiguration config)
        {
            _context = context;
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] UserRegisterDto userDto)
        {
            if (userDto == null || string.IsNullOrWhiteSpace(userDto.Email) || 
                string.IsNullOrWhiteSpace(userDto.Password) || string.IsNullOrWhiteSpace(userDto.Name))
            {
                return BadRequest("All fields (Email, Password, Name) are required and cannot be empty.");
            }

            if (_context.Users.Any(u => u.Email == userDto.Email))
                return BadRequest("Email already exists");

            var user = new User
            {
                Email = userDto.Email,
                PasswordHash = AuthHelper.HashPassword(userDto.Password),
                Name = userDto.Name,
                Roles = "Landlord,Client"
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();
            var token = AuthHelper.GenerateToken(user, _config);
            return Ok(new { Id = user.Id, user.Email, user.Name, user.Roles, Token = token });
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] UserLoginDto userDto)
        {
            if (userDto == null || string.IsNullOrWhiteSpace(userDto.Email) || 
                string.IsNullOrWhiteSpace(userDto.Password))
            {
                return BadRequest("Email and Password are required and cannot be empty.");
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == userDto.Email);
            if (user == null || !AuthHelper.VerifyPassword(userDto.Password, user.PasswordHash))
                return Unauthorized("Invalid credentials");

            var token = AuthHelper.GenerateToken(user, _config);
            return Ok(new { Id = user.Id, user.Email, user.Name, user.Roles, Token = token });
        }

        [HttpGet("me")]
        [Authorize]
        public async Task<IActionResult> GetCurrentUser()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userIdClaim == null || !int.TryParse(userIdClaim, out int userId))
            {
                return Unauthorized("Invalid user ID in token");
            }

            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                return NotFound("User not found");
            }

            return Ok(new 
            { 
                Id = user.Id, 
                user.Email, 
                user.Name, 
                user.Roles, 
                user.AvatarUrl 
            });
        }

        [HttpPost("upload-avatar")]
        [Authorize]
        public async Task<IActionResult> UploadAvatar(IFormFile avatar)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userIdClaim == null || !int.TryParse(userIdClaim, out int userId))
            {
                return Unauthorized("Invalid user ID in token");
            }

            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                return NotFound("User not found");
            }

            if (avatar != null && avatar.Length > 0)
            {
                var fileName = $"{userId}_{Guid.NewGuid()}.jpg";
                var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/uploads/avatars", fileName);
                Console.WriteLine($"Saving avatar to: {filePath}");
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await avatar.CopyToAsync(stream);
                }
                user.AvatarUrl = $"/uploads/avatars/{fileName}";
                Console.WriteLine($"Avatar URL set to: {user.AvatarUrl}");
                await _context.SaveChangesAsync();
            }

            var token = AuthHelper.GenerateToken(user, _config);
            return Ok(new { Id = user.Id, user.Email, user.Name, user.Roles, user.AvatarUrl, Token = token });
        }

        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Email))
                return BadRequest("Email is required and cannot be empty.");

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == dto.Email);
            if (user == null)
                return NotFound("User with this email does not exist.");

            var resetCode = Guid.NewGuid().ToString("N").Substring(0, 6).ToUpper();
            _resetCodes[dto.Email] = resetCode;

            await SendResetCodeEmail(dto.Email, resetCode);
            return Ok("Reset code has been sent to your email.");
        }

        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Code) || 
                string.IsNullOrWhiteSpace(dto.NewPassword))
                return BadRequest("Email, Code, and NewPassword are required and cannot be empty.");

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == dto.Email);
            if (user == null)
                return NotFound("User with this email does not exist.");

            if (!_resetCodes.TryGetValue(dto.Email, out var storedCode) || storedCode != dto.Code)
                return BadRequest("Invalid or expired reset code.");

            user.PasswordHash = AuthHelper.HashPassword(dto.NewPassword);
            await _context.SaveChangesAsync();
            _resetCodes.Remove(dto.Email);

            return Ok("Password has been successfully reset.");
        }

        private async Task SendResetCodeEmail(string email, string code)
        {
            // Безопасно получаем настройки SMTP
            string? smtpUsername = _config["EmailSettings:SmtpUsername"];
            string? smtpPassword = _config["EmailSettings:SmtpPassword"];
            string? smtpServer = _config["EmailSettings:SmtpServer"];
            string? smtpPortStr = _config["EmailSettings:SmtpPort"];

            // Проверяем наличие всех настроек
            if (string.IsNullOrWhiteSpace(smtpUsername) || string.IsNullOrWhiteSpace(smtpPassword) || 
                string.IsNullOrWhiteSpace(smtpServer) || string.IsNullOrWhiteSpace(smtpPortStr))
            {
                throw new InvalidOperationException("SMTP configuration is missing in appsettings.json.");
            }

            // Парсим порт с обработкой null
            if (!int.TryParse(smtpPortStr, out int smtpPort))
            {
                throw new InvalidOperationException("Invalid SMTP port configuration in appsettings.json.");
            }

            var fromAddress = new MailAddress(smtpUsername, "Airbnb Lite");
            var toAddress = new MailAddress(email);
            var subject = "Password Reset Code";
            var body = $"Your password reset code is: <strong>{code}</strong>. Use it to reset your password.";

            var smtp = new SmtpClient
            {
                Host = smtpServer,
                Port = smtpPort,
                EnableSsl = true,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(smtpUsername, smtpPassword)
            };

            using var message = new MailMessage(fromAddress, toAddress)
            {
                Subject = subject,
                Body = body,
                IsBodyHtml = true
            };

            await smtp.SendMailAsync(message);
        }
    }

    public class UserRegisterDto
    {
        public required string Email { get; set; }
        public required string Password { get; set; }
        public required string Name { get; set; }
    }

    public class UserLoginDto
    {
        public required string Email { get; set; }
        public required string Password { get; set; }
    }

    public class ForgotPasswordDto
    {
        public required string Email { get; set; }
    }

    public class ResetPasswordDto
    {
        public required string Email { get; set; }
        public required string Code { get; set; }
        public required string NewPassword { get; set; }
    }
}