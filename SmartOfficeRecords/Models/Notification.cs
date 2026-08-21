namespace SmartOfficeRecords.Models
{
    public class Notification
    {
        public int NotificationId { get; set; }
        public int ApplicantId { get; set; }
        public int? AppointmentId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public bool IsRead { get; set; }
        public DateTime DateCreated { get; set; } = DateTime.Now;
    }
}