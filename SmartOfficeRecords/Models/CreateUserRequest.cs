using Microsoft.AspNetCore.Http;

namespace SmartOfficeRecords.Models
{
    public class CreateUserRequest
    {
        // Initialized to string.Empty so these don't trigger CS8618
        // ("non-nullable property must contain a non-null value") warnings
        // under nullable reference types. They're all required form fields,
        // so an empty-string default is safe — the null checks in the
        // controller catch anything left blank.
        public string FullName { get; set; } = string.Empty;
        public string ContactNumber { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string ConfirmPassword { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;

        // Nullable because it's genuinely optional — no warning here since
        // the ? already tells the compiler null is expected.
        public IFormFile? ProfileImage { get; set; }
    }
}