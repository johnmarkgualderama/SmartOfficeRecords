using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartOfficeRecords.Models
{
    [Table("Staff")]
    public class Staff
    {
        [Key]
        public int StaffId { get; set; }

        [Required]
        public string Username { get; set; } = string.Empty;

        [Required]
        public string Password { get; set; } = string.Empty;

        [Required]
        public string FullName { get; set; } = string.Empty;

        public string? ContactNumber { get; set; }

        public string? Email { get; set; }

        public string? ProfileImage { get; set; }

        public DateTime DateCreated { get; set; }

        public bool IsActive { get; set; } = true;
        public DateTime? LastLogin { get; set; }
    }
}