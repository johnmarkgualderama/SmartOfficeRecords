namespace SmartOfficeRecords.Models
{
    public class EditAppointmentRequest
    {
        public int AppointmentId { get; set; }
        public string EmailAddress { get; set; } = string.Empty;
        public string PreferredDate { get; set; } = string.Empty;
        public string PreferredTime { get; set; } = string.Empty;
    }
}
