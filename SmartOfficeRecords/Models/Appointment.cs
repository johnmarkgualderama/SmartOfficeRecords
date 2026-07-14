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

        // Pending, Approved, Completed, Rejected
        public string Status { get; set; } = "Pending";

        public DateTime DateRequested { get; set; }

        // Lets us access applicant.FullName etc. directly from an Appointment object
        [ForeignKey("ApplicantId")]
        public ApplicantRegister? Applicant { get; set; }
    }
}