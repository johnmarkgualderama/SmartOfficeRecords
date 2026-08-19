using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartOfficeRecords.Models
{
    [Table("Appointment")]
    public class Appointment
    {
        [Key]
        public int AppointmentId { get; set; }

        [Required]
        public int ApplicantId { get; set; }

        [Required]
        public string Purpose { get; set; } = string.Empty;

        [Required]
        public DateTime AppointmentDate { get; set; }

        [Required]
        public TimeSpan AppointmentTime { get; set; }

        public string Status { get; set; } = "Pending";

        public DateTime DateRequested { get; set; }

        // Set the moment an admin approves the appointment — used to order
        // Recent Applicants by "who got approved first" rather than by
        // submission time.
        public DateTime? DateApproved { get; set; }

        // ----- NEW fields for the Book Interview form -----
        public string? ContactNumber { get; set; }
        public string? Email { get; set; }
        public string? ResumeFile { get; set; }

        public string? ValidIDFile { get; set; }
        public string? AdditionalNotes { get; set; }
        public DateTime? DateCompleted { get; set; }

        [ForeignKey("ApplicantId")]
        public ApplicantRegister? Applicant { get; set; }
    }
}