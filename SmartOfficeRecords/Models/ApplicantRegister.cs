using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartOfficeRecords.Models
{
    [Table("ApplicantRegister")]
    public class ApplicantRegister
    {
        [Key]
        public int ApplicantId { get; set; }

        [Required]
        public string FullName { get; set; } = string.Empty;

        [Required]
        public string Username { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string ContactNumber { get; set; } = string.Empty;

        [Required]
        public DateTime Birthdate { get; set; }

        public string? InvitedBy { get; set; }          // optional in DB → nullable

        [Required]
        public string Password { get; set; } = string.Empty;

        [NotMapped]
        public string? ConfirmPassword { get; set; }    // not even saved to DB

        public string? ProfileImage { get; set; }        // optional in DB → nullable

        public string? ResetCode { get; set; }            // optional in DB → nullable
        public DateTime? ResetCodeExpiry { get; set; }
    }
}