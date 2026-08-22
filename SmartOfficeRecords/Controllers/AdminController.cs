using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SmartOfficeRecords.Data;
using SmartOfficeRecords.Models;
using SmartOfficeRecords.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SmartOfficeRecords.Controllers
{
    public class AdminController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly EmailService _emailService;

        public AdminController(ApplicationDbContext context, EmailService emailService)
        {
            _context = context;
            _emailService = emailService;
        }

        // ================== LOGIN ==================

        [HttpGet]
        public ActionResult Login()
        {
            return View();
        }

        [HttpPost]
        public ActionResult Login(string Username, string Password)
        {
            if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
            {
                ViewBag.Error = "Please enter both Username and Password.";
                return View();
            }

            string hashedPassword = HashPassword(Password);

            var admin = _context.Admins
                .FirstOrDefault(a => a.Username == Username && a.Password == hashedPassword);

            if (admin == null)
            {
                ViewBag.Error = "Invalid Username or Password";
                return View();
            }

            if (!admin.IsActive)
            {
                ViewBag.Error = "This account has been deactivated. Contact an administrator.";
                return View();
            }

            admin.LastLogin = DateTime.Now;
            _context.SaveChanges();

            HttpContext.Session.SetInt32("AdminId", admin.AdminId);
            HttpContext.Session.SetString("AdminName", admin.FullName);
            HttpContext.Session.SetString("AdminUsername", admin.Username);

            return RedirectToAction("Dashboard");
        }

        public ActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Login");
        }

        // ================== DASHBOARD & PAGES ==================

        public IActionResult Dashboard()
        {
            if (HttpContext.Session.GetInt32("AdminId") == null)
                return RedirectToAction("Login");

            var today = DateTime.Today;

            var todaysAppointmentsRaw = _context.Appointments
                .Include(a => a.Applicant)
                .Where(a => a.DateRequested.Date == today)
                .OrderBy(a => a.DateRequested)
                .ToList();

            ViewBag.TotalRequests = todaysAppointmentsRaw.Count;
            ViewBag.Pending = todaysAppointmentsRaw.Count(a => a.Status == "Pending");
            ViewBag.Approved = todaysAppointmentsRaw.Count(a => a.Status == "Approved");
            ViewBag.Completed = todaysAppointmentsRaw.Count(a => a.Status == "Completed");
            ViewBag.Rejected = todaysAppointmentsRaw.Count(a => a.Status == "Rejected");

            var pendingApplicants = todaysAppointmentsRaw
                .Where(a => a.Status == "Pending")
                .Select(a => new
                {
                    a.AppointmentId,
                    ApplicantName = a.Applicant!.FullName,
                    ApplicantEmail = a.Applicant!.Email,
                    a.DateRequested,
                    a.Status
                })
                .ToList();

            ViewBag.PendingApplicants = pendingApplicants;

            var displayIdMap = new Dictionary<int, string>();
            var orderedForNumbering = todaysAppointmentsRaw.OrderBy(a => a.DateRequested).ToList();
            for (int i = 0; i < orderedForNumbering.Count; i++)
            {
                displayIdMap[orderedForNumbering[i].AppointmentId] = "AP" + (i + 1).ToString("D3");
            }

            string[] avatarColors = { "#B91C1C", "#111827", "#0EA5A0", "#7C3AED", "#F59E0B", "#2563EB" };

            ViewBag.TodayApplicants = todaysAppointmentsRaw
                .Select((a, index) => new
                {
                    a.AppointmentId,
                    DisplayId = displayIdMap.ContainsKey(a.AppointmentId) ? displayIdMap[a.AppointmentId] : "AP000",
                    ApplicantName = a.Applicant!.FullName,
                    ApplicantEmail = a.Applicant!.Email,
                    DateAppliedFormatted = a.DateRequested.ToString("MMM dd, yyyy"),
                    a.Status,
                    Initials = GetInitials(a.Applicant!.FullName),
                    AvatarColor = avatarColors[index % avatarColors.Length]
                })
                .ToList();

            var todaysScheduledAppointments = _context.Appointments
                .Include(a => a.Applicant)
                .Where(a => a.AppointmentDate.Date == today && (a.Status == "Approved" || a.Status == "Completed"))
                .OrderBy(a => a.DateApproved ?? a.DateRequested)
                .ToList();

            var approvalDisplayIdMap = new Dictionary<int, string>();
            for (int i = 0; i < todaysScheduledAppointments.Count; i++)
            {
                approvalDisplayIdMap[todaysScheduledAppointments[i].AppointmentId] = "AP" + (i + 1).ToString("D3");
            }

            ViewBag.RecentApplicants = todaysScheduledAppointments
                .Select(a => new
                {
                    a.AppointmentId,
                    DisplayId = approvalDisplayIdMap.ContainsKey(a.AppointmentId) ? approvalDisplayIdMap[a.AppointmentId] : "AP000",
                    ApplicantName = a.Applicant!.FullName,
                    a.Status
                })
                .ToList();

            int currentYear = DateTime.Today.Year;

            var availableYears = _context.Appointments
                .Select(a => a.DateRequested.Year)
                .Distinct()
                .OrderByDescending(y => y)
                .ToList();

            if (!availableYears.Contains(currentYear))
            {
                availableYears.Insert(0, currentYear);
                availableYears = availableYears.OrderByDescending(y => y).ToList();
            }

            ViewBag.AvailableYears = availableYears;
            ViewBag.CurrentYear = currentYear;

            return View();
        }

        private string GetInitials(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName)) return "?";
            var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
                return $"{parts[0][0]}{parts[^1][0]}".ToUpper();
            if (parts[0].Length >= 2)
                return parts[0].Substring(0, 2).ToUpper();
            return parts[0].ToUpper();
        }

        // ================== NOTIFICATIONS ==================
        // Shared helper: logs a Notification row for the applicant and, if an
        // email address is available, sends the matching email. Called from
        // the three status-change actions below.
        private void NotifyApplicant(int applicantId, int appointmentId, string type, string title, string message, string toEmail, string emailSubject, string emailBody)
        {
            _context.Notifications.Add(new Notification
            {
                ApplicantId = applicantId,
                AppointmentId = appointmentId,
                Title = title,
                Message = message,
                Type = type,
                IsRead = false,
                DateCreated = DateTime.Now
            });
            _context.SaveChanges();

            if (!string.IsNullOrWhiteSpace(toEmail))
            {
                _emailService.SendEmail(toEmail, emailSubject, emailBody);
            }
        }

        [HttpPost]
        public ActionResult ApproveAppointment(int AppointmentId)
        {
            if (HttpContext.Session.GetInt32("AdminId") == null)
                return RedirectToAction("Login");

            var appointment = _context.Appointments
                .Include(a => a.Applicant)
                .FirstOrDefault(a => a.AppointmentId == AppointmentId);

            if (appointment != null)
            {
                appointment.Status = "Approved";
                appointment.DateApproved = DateTime.Now;
                _context.SaveChanges();

                NotifyApplicant(
                    appointment.Applicant!.ApplicantId,
                    appointment.AppointmentId,
                    "Approved",
                    "Appointment Approved",
                    "Your interview request has been approved.",
                    appointment.Applicant.Email,
                    "Your SORS Appointment Has Been Approved",
                    $"<p>Hi {appointment.Applicant.FullName},</p><p>Your appointment request has been <strong>approved</strong>. Please check your dashboard for details.</p>"
                );
            }

            return RedirectToAction("Dashboard");
        }

        [HttpPost]
        public ActionResult CompleteAppointment(int AppointmentId)
        {
            if (HttpContext.Session.GetInt32("AdminId") == null)
                return RedirectToAction("Login");

            var appointment = _context.Appointments
                .Include(a => a.Applicant)
                .FirstOrDefault(a => a.AppointmentId == AppointmentId);

            if (appointment != null && appointment.Status == "Approved")
            {
                appointment.Status = "Completed";
                appointment.DateCompleted = DateTime.Now;
                _context.SaveChanges();

                NotifyApplicant(
                    appointment.Applicant!.ApplicantId,
                    appointment.AppointmentId,
                    "Completed",
                    "Process Completed",
                    "Your document verification process has been completed.",
                    appointment.Applicant.Email,
                    "Your SORS Request Has Been Completed",
                    $"<p>Hi {appointment.Applicant.FullName},</p><p>Your request has been marked as <strong>completed</strong>. Thank you for using SORS.</p>"
                );
            }

            return RedirectToAction("Dashboard");
        }

        [HttpPost]
        public ActionResult RejectAppointment(int AppointmentId)
        {
            if (HttpContext.Session.GetInt32("AdminId") == null)
                return RedirectToAction("Login");

            var appointment = _context.Appointments
                .Include(a => a.Applicant)
                .FirstOrDefault(a => a.AppointmentId == AppointmentId);

            if (appointment != null)
            {
                appointment.Status = "Rejected";
                _context.SaveChanges();

                NotifyApplicant(
                    appointment.Applicant!.ApplicantId,
                    appointment.AppointmentId,
                    "Rejected",
                    "Appointment Rejected",
                    "Your interview request was not approved.",
                    appointment.Applicant.Email,
                    "Update on Your SORS Appointment Request",
                    $"<p>Hi {appointment.Applicant.FullName},</p><p>Unfortunately, your appointment request was <strong>not approved</strong> at this time. Please contact our office for more information.</p>"
                );
            }

            return RedirectToAction("Dashboard");
        }

        public ActionResult ViewAppointment(int id)
        {
            if (HttpContext.Session.GetInt32("AdminId") == null)
                return RedirectToAction("Login");

            var appointment = _context.Appointments
                .Include(a => a.Applicant)
                .FirstOrDefault(a => a.AppointmentId == id);

            if (appointment == null)
                return RedirectToAction("Dashboard");

            return View(appointment);
        }

        [HttpGet]
        public JsonResult GetRequestChartData(string range, int? year)
        {
            if (HttpContext.Session.GetInt32("AdminId") == null)
                return Json(new { error = "Not logged in" });

            var now = DateTime.Now;
            int targetYear = year ?? now.Year;
            bool isCurrentYear = targetYear == now.Year;

            List<object> labels = new List<object>();
            List<int> counts = new List<int>();

            if (range == "week")
            {
                var startOfWeek = now.AddDays(-(int)now.DayOfWeek + (now.DayOfWeek == DayOfWeek.Sunday ? -6 : 1));

                for (int i = 0; i < 7; i++)
                {
                    var day = startOfWeek.AddDays(i);
                    int count = _context.Appointments.Count(a => a.DateRequested.Date == day.Date);

                    labels.Add(new string[] { day.ToString("ddd"), day.ToString("MM-dd") });
                    counts.Add(count);
                }
            }
            else if (range == "month")
            {
                int monthToUse = now.Month;
                int daysInMonth = DateTime.DaysInMonth(targetYear, monthToUse);

                for (int day = 1; day <= daysInMonth; day++)
                {
                    var date = new DateTime(targetYear, monthToUse, day);
                    int count = _context.Appointments.Count(a => a.DateRequested.Date == date.Date);

                    labels.Add(new string[] { day.ToString(), date.ToString("MM-dd") });
                    counts.Add(count);
                }
            }
            else if (range == "year")
            {
                for (int month = 1; month <= 12; month++)
                {
                    int count = _context.Appointments.Count(a =>
                        a.DateRequested.Year == targetYear && a.DateRequested.Month == month);

                    labels.Add(new DateTime(targetYear, month, 1).ToString("MMMM"));
                    counts.Add(count);
                }
            }

            return Json(new { labels, counts, year = targetYear, isCurrentYear });
        }

        public ActionResult RecordsManagement(string search, int page = 1)
        {
            if (HttpContext.Session.GetInt32("AdminId") == null)
                return RedirectToAction("Login");

            var allCompletedOrdered = _context.Appointments
                .Where(a => a.Status == "Completed")
                .OrderBy(a => a.DateCompleted ?? a.DateRequested)
                .Select(a => a.AppointmentId)
                .ToList();

            var recordIdMap = new Dictionary<int, string>();
            for (int i = 0; i < allCompletedOrdered.Count; i++)
            {
                recordIdMap[allCompletedOrdered[i]] = "RC" + (i + 1).ToString("D3");
            }

            var query = _context.Appointments
                .Include(a => a.Applicant)
                .Where(a => a.Status == "Completed")
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(a => a.Applicant!.FullName.Contains(search));
            }

            var filteredRecords = query.ToList();

            var distinctDatesDescending = filteredRecords
                .Select(a => a.DateRequested.Date)
                .Distinct()
                .ToList();

            var today = DateTime.Today;
            if (!distinctDatesDescending.Contains(today))
            {
                distinctDatesDescending.Add(today);
            }

            distinctDatesDescending = distinctDatesDescending
                .OrderByDescending(d => d)
                .ToList();

            int totalPages = distinctDatesDescending.Count;
            if (totalPages < 1) totalPages = 1;
            if (page < 1) page = 1;
            if (page > totalPages) page = totalPages;

            DateTime? currentPageDate = distinctDatesDescending.Count > 0
                ? distinctDatesDescending[page - 1]
                : (DateTime?)null;

            var recordsForThisDate = currentPageDate == null
                ? new List<Appointment>()
                : filteredRecords
                    .Where(a => a.DateRequested.Date == currentPageDate.Value)
                    .OrderByDescending(a => a.DateRequested)
                    .ToList();

            int totalCount = recordsForThisDate.Count;

            var records = recordsForThisDate
                .Select(a => new
                {
                    a.AppointmentId,
                    RecordId = recordIdMap.ContainsKey(a.AppointmentId) ? recordIdMap[a.AppointmentId] : "RC000",
                    ApplicantName = a.Applicant!.FullName,
                    a.Status,
                    AppointmentDateFormatted = a.DateRequested.ToString("MMM dd, yyyy")
                })
                .ToList();

            ViewBag.Records = records;
            ViewBag.SearchTerm = search;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalCount = totalCount;
            ViewBag.PageSize = totalCount;
            ViewBag.CurrentDateLabel = currentPageDate?.ToString("MMM dd, yyyy") ?? "—";

            return View();
        }

        // ================== REQUEST MANAGEMENT ==================
        // Strongly-typed result for BuildRequestsData, shared by the normal
        // page load (RequestManagement) and the live-search AJAX endpoint
        // (SearchRequests) so both stay in sync automatically. This is what
        // powers the "type in the search box, results update immediately"
        // behavior — no form submit, no page reload.
        //
        // Behavior:
        //  - "All Status" + no search  -> old day-by-day paging (one
        //    calendar date per page, most recent date first).
        //  - Any search text OR a specific status selected -> flat list
        //    across ALL dates, newest DateRequested first, paged 10 rows
        //    at a time. This is what makes searching/filtering surface
        //    past-dated records instead of being stuck on today's page.
        private class RequestManagementResult
        {
            public List<object> Requests { get; set; } = new();
            public string SelectedStatus { get; set; } = "All Status";
            public int CurrentPage { get; set; }
            public int TotalPages { get; set; }
            public int TotalCount { get; set; }
            public int StartItem { get; set; }
            public int EndItem { get; set; }
            public string CurrentDateLabel { get; set; } = "—";
        }

        private RequestManagementResult BuildRequestsData(string search, string status, int page)
        {
            var allAppointmentsForNumbering = _context.Appointments
                .OrderBy(a => a.DateRequested)
                .ToList();

            var displayIdMap = new Dictionary<int, string>();
            foreach (var dayGroup in allAppointmentsForNumbering.GroupBy(a => a.DateRequested.Date))
            {
                var ordered = dayGroup.OrderBy(a => a.DateRequested).ToList();
                for (int i = 0; i < ordered.Count; i++)
                {
                    displayIdMap[ordered[i].AppointmentId] = "AP" + (i + 1).ToString("D3");
                }
            }

            var query = _context.Appointments
                .Include(a => a.Applicant)
                .AsQueryable();

            bool hasSearch = !string.IsNullOrWhiteSpace(search);
            bool hasStatusFilter = !string.IsNullOrWhiteSpace(status) && status != "All Status";

            if (hasSearch)
            {
                query = query.Where(a => a.Applicant!.FullName.Contains(search));
            }

            if (hasStatusFilter)
            {
                query = query.Where(a => a.Status == status);
            }

            var filteredAppointments = query.ToList();

            List<Appointment> appointmentsToShow;
            int totalPages, totalCount, startItem, endItem;
            string currentDateLabel;

            bool useFlatList = hasSearch || hasStatusFilter;

            if (useFlatList)
            {
                const int pageSize = 10;

                var orderedFlat = filteredAppointments
                    .OrderByDescending(a => a.DateRequested)
                    .ToList();

                totalCount = orderedFlat.Count;
                totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
                if (totalPages < 1) totalPages = 1;
                if (page < 1) page = 1;
                if (page > totalPages) page = totalPages;

                appointmentsToShow = orderedFlat
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                startItem = totalCount == 0 ? 0 : ((page - 1) * pageSize) + 1;
                endItem = Math.Min(page * pageSize, totalCount);
                currentDateLabel = "All Dates";
            }
            else
            {
                var distinctDatesDescending = filteredAppointments
                    .Select(a => a.DateRequested.Date)
                    .Distinct()
                    .ToList();

                var today = DateTime.Today;
                if (!distinctDatesDescending.Contains(today))
                {
                    distinctDatesDescending.Add(today);
                }

                distinctDatesDescending = distinctDatesDescending
                    .OrderByDescending(d => d)
                    .ToList();

                totalPages = distinctDatesDescending.Count;
                if (totalPages < 1) totalPages = 1;
                if (page < 1) page = 1;
                if (page > totalPages) page = totalPages;

                DateTime? currentPageDate = distinctDatesDescending.Count > 0
                    ? distinctDatesDescending[page - 1]
                    : (DateTime?)null;

                appointmentsToShow = currentPageDate == null
                    ? new List<Appointment>()
                    : filteredAppointments
                        .Where(a => a.DateRequested.Date == currentPageDate.Value)
                        .OrderBy(a => a.Status == "Pending" ? 0 : 1)
                        .ThenBy(a => a.DateRequested)
                        .ToList();

                totalCount = appointmentsToShow.Count;
                startItem = totalCount == 0 ? 0 : 1;
                endItem = totalCount;
                currentDateLabel = currentPageDate?.ToString("MMM dd, yyyy") ?? "—";
            }

            var requests = appointmentsToShow
                .Select(a => new
                {
                    a.AppointmentId,
                    DisplayId = displayIdMap.ContainsKey(a.AppointmentId) ? displayIdMap[a.AppointmentId] : "AP000",
                    ApplicantName = a.Applicant!.FullName,
                    ApplicantEmail = a.Applicant!.Email,
                    Initials = GetInitials(a.Applicant!.FullName),
                    a.Status,
                    DateAppliedFormatted = a.DateRequested.ToString("MMM dd, yyyy"),
                    Email = a.Email,
                    ContactNumber = a.ContactNumber,
                    AppointmentDateFormatted = a.AppointmentDate.ToString("MMM dd, yyyy"),
                    AppointmentTimeFormatted = DateTime.Today.Add(a.AppointmentTime).ToString("hh:mm tt"),
                    DateSubmittedFormatted = a.DateRequested.ToString("MMM dd, yyyy hh:mm tt"),
                    AdditionalNotes = string.IsNullOrWhiteSpace(a.AdditionalNotes) ? "No additional notes." : a.AdditionalNotes,
                    // a.ResumeFile / a.ValidIDFile already hold a full
                    // app-relative path (e.g. "/Uploads/Resumes/xyz.pdf") as
                    // written in RCreateRequest below, so this just resolves
                    // "~" + that path. Prepending an extra "/Resumes/" or
                    // "/ValidIDs/" segment here would double up the path and
                    // break the link.
                    ResumeUrl = string.IsNullOrEmpty(a.ResumeFile) ? "" : Url.Content("~/" + a.ResumeFile),
                    ValidIdUrl = string.IsNullOrEmpty(a.ValidIDFile) ? "" : Url.Content("~/" + a.ValidIDFile)
                })
                .ToList<object>();

            return new RequestManagementResult
            {
                Requests = requests,
                SelectedStatus = string.IsNullOrWhiteSpace(status) ? "All Status" : status,
                CurrentPage = page,
                TotalPages = totalPages,
                TotalCount = totalCount,
                StartItem = startItem,
                EndItem = endItem,
                CurrentDateLabel = currentDateLabel
            };
        }

        [HttpGet]
        public ActionResult RequestManagement(string search, string status, int page = 1)
        {
            if (HttpContext.Session.GetInt32("AdminId") == null)
                return RedirectToAction("Login");

            var data = BuildRequestsData(search, status, page);

            ViewBag.Requests = data.Requests;
            ViewBag.SearchTerm = search;
            ViewBag.SelectedStatus = data.SelectedStatus;
            ViewBag.CurrentPage = data.CurrentPage;
            ViewBag.TotalPages = data.TotalPages;
            ViewBag.TotalCount = data.TotalCount;
            ViewBag.StartItem = data.StartItem;
            ViewBag.EndItem = data.EndItem;
            ViewBag.CurrentDateLabel = data.CurrentDateLabel;

            return View();
        }

        // Live-search endpoint used by RequestManagement.cshtml's JS —
        // returns the same data shape as the page load above, as JSON,
        // so the table can be filtered/paginated without a full reload as
        // the admin types.
        [HttpGet]
        public JsonResult SearchRequests(string search, string status, int page = 1)
        {
            if (HttpContext.Session.GetInt32("AdminId") == null)
                return Json(new { error = "Not logged in" });

            var data = BuildRequestsData(search, status, page);
            return Json(data);
        }

        public ActionResult ReportsManagement()
        {
            if (HttpContext.Session.GetInt32("AdminId") == null)
                return RedirectToAction("Login");

            return View();
        }

        public ActionResult UsersManagement(string search, string role, int page = 1)
        {
            if (HttpContext.Session.GetInt32("AdminId") == null)
                return RedirectToAction("Login");

            const int pageSize = 5;

            var adminUsers = _context.Admins
                .Select(a => new
                {
                    Id = a.AdminId,
                    RoleTag = "Admin",
                    a.FullName,
                    a.Email,
                    a.IsActive,
                    a.LastLogin,
                    SortDate = (DateTime?)a.LastLogin ?? a.DateCreated
                });

            var staffUsers = _context.Staffs
                .Select(s => new
                {
                    Id = s.StaffId,
                    RoleTag = "Staff",
                    s.FullName,
                    s.Email,
                    s.IsActive,
                    s.LastLogin,
                    SortDate = (DateTime?)s.LastLogin ?? s.DateCreated
                });

            var combined = adminUsers.Concat(staffUsers).ToList();

            if (!string.IsNullOrWhiteSpace(search))
            {
                combined = combined.Where(u =>
                    (u.FullName ?? "").Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    (u.Email ?? "").Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            if (!string.IsNullOrWhiteSpace(role) && role != "All Roles")
            {
                combined = combined.Where(u => u.RoleTag == role).ToList();
            }

            combined = combined.OrderByDescending(u => u.SortDate).ToList();

            int totalCount = combined.Count;
            int totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
            if (totalPages < 1) totalPages = 1;
            if (page < 1) page = 1;
            if (page > totalPages) page = totalPages;

            var pageItems = combined
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select((u, index) => new
                {
                    DisplayId = "U" + ((page - 1) * pageSize + index + 1).ToString("D4"),
                    u.Id,
                    u.RoleTag,
                    u.FullName,
                    u.Email,
                    u.IsActive,
                    LastActiveText = u.LastLogin == null
                        ? "Never logged in"
                        : TimeAgo(u.LastLogin.Value)
                })
                .ToList();

            ViewBag.Users = pageItems;
            ViewBag.SearchTerm = search;
            ViewBag.SelectedRole = string.IsNullOrWhiteSpace(role) ? "All Roles" : role;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalCount = totalCount;

            return View();
        }

        [HttpPost]
        public JsonResult DeactivateUser(int id, string role)
        {
            if (HttpContext.Session.GetInt32("AdminId") == null)
                return Json(new { success = false, message = "Session expired." });

            if (role == "Admin")
            {
                var admin = _context.Admins.Find(id);
                if (admin == null) return Json(new { success = false, message = "Admin not found." });
                admin.IsActive = !admin.IsActive;
                _context.SaveChanges();
                return Json(new { success = true, isActive = admin.IsActive });
            }
            else if (role == "Staff")
            {
                var staff = _context.Staffs.Find(id);
                if (staff == null) return Json(new { success = false, message = "Staff not found." });
                staff.IsActive = !staff.IsActive;
                _context.SaveChanges();
                return Json(new { success = true, isActive = staff.IsActive });
            }

            return Json(new { success = false, message = "Invalid role." });
        }

        private string TimeAgo(DateTime dateTime)
        {
            var span = DateTime.Now - dateTime;
            if (span.TotalMinutes < 1) return "Just now";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} minutes ago";
            if (span.TotalHours < 24) return $"{(int)span.TotalHours} hours ago";
            if (span.TotalDays < 30) return $"{(int)span.TotalDays} days ago";
            return dateTime.ToString("MMM dd, yyyy");
        }

        public ActionResult Settings()
        {
            if (HttpContext.Session.GetInt32("AdminId") == null)
                return RedirectToAction("Login");

            return View();
        }

        public ActionResult SAccountSettings()
        {
            if (HttpContext.Session.GetInt32("AdminId") == null)
                return RedirectToAction("Login");

            int adminId = HttpContext.Session.GetInt32("AdminId")!.Value;
            var adminRecord = _context.Admins.Find(adminId);

            var admin = new AdminViewModel
            {
                FullName = adminRecord?.FullName ?? "Unknown",
                Username = adminRecord?.Username ?? "Unknown",
                Email = "admin@sors.com",
                Phone = "09123456789"
            };

            return View(admin);
        }

        // ================== CREATE REQUEST (WALK-IN) ==================

        [HttpGet]
        public ActionResult RCreateRequest()
        {
            if (HttpContext.Session.GetInt32("AdminId") == null)
                return RedirectToAction("Login");

            return View();
        }

        [HttpPost]
        public ActionResult RCreateRequest(
            string FullName,
            string ContactNumber,
            string Email,
            IFormFile ResumeFile,
            IFormFile ValidIdFile,
            string PreferredDate,
            string PreferredTime,
            string AdditionalNotes)
        {
            if (HttpContext.Session.GetInt32("AdminId") == null)
                return RedirectToAction("Login");

            if (string.IsNullOrWhiteSpace(FullName) ||
                string.IsNullOrWhiteSpace(ContactNumber) ||
                string.IsNullOrWhiteSpace(Email) ||
                ResumeFile == null || ResumeFile.Length == 0 ||
                ValidIdFile == null || ValidIdFile.Length == 0 ||
                string.IsNullOrWhiteSpace(PreferredDate) ||
                string.IsNullOrWhiteSpace(PreferredTime))
            {
                TempData["Error"] = "Please fill in all required fields.";
                return RedirectToAction("RCreateRequest");
            }

            if (!DateTime.TryParse(PreferredDate, out DateTime parsedDate))
            {
                TempData["Error"] = "Invalid interview date.";
                return RedirectToAction("RCreateRequest");
            }

            if (!DateTime.TryParse(PreferredTime, out DateTime parsedTimeValue))
            {
                TempData["Error"] = "Invalid interview time.";
                return RedirectToAction("RCreateRequest");
            }
            TimeSpan parsedTime = parsedTimeValue.TimeOfDay;

            // ---- Reject duplicate applicants ----
            // Any existing ApplicantRegister with this email blocks the
            // request entirely — no reuse, no silent linking. (The
            // database also enforces this via a UNIQUE constraint on
            // Email, but checking here gives a clean error message
            // instead of an unhandled SqlException.)
            bool emailExists = _context.ApplicantRegisters.Any(a => a.Email == Email);

            if (emailExists)
            {
                TempData["Error"] = "A request with this Email already exists.";
                return RedirectToAction("RCreateRequest");
            }

            // ---- Create the walk-in applicant record ----
            // Walk-in applicants don't register an account, so this is a
            // bare ApplicantRegister row just to satisfy the Appointment
            // foreign key. Username/Password are placeholders, not meant
            // for login.
            string baseUsername = "walkin_" + Email.Split('@')[0];
            string username = baseUsername;
            int suffix = 1;
            while (_context.ApplicantRegisters.Any(a => a.Username == username))
            {
                username = baseUsername + suffix;
                suffix++;
            }

            var applicant = new ApplicantRegister
            {
                FullName = FullName,
                Username = username,
                Email = Email,
                ContactNumber = ContactNumber,
                Birthdate = new DateTime(1900, 1, 1),               // sentinel — unknown for walk-ins
                InvitedBy = "Walk-in (created by Admin)",
                Password = HashPassword(Guid.NewGuid().ToString())  // unusable placeholder
            };

            _context.ApplicantRegisters.Add(applicant);
            _context.SaveChanges(); // need ApplicantId generated before linking the appointment

            // ---- Save uploaded files ----
            string uploadsRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Uploads");
            string resumeFolder = Path.Combine(uploadsRoot, "Resumes");
            string validIdFolder = Path.Combine(uploadsRoot, "ValidIDs");

            if (!Directory.Exists(resumeFolder)) Directory.CreateDirectory(resumeFolder);
            if (!Directory.Exists(validIdFolder)) Directory.CreateDirectory(validIdFolder);

            string resumeFileName = Guid.NewGuid().ToString() + Path.GetExtension(ResumeFile.FileName);
            using (var stream = new FileStream(Path.Combine(resumeFolder, resumeFileName), FileMode.Create))
            {
                ResumeFile.CopyTo(stream);
            }

            string validIdFileName = Guid.NewGuid().ToString() + Path.GetExtension(ValidIdFile.FileName);
            using (var stream = new FileStream(Path.Combine(validIdFolder, validIdFileName), FileMode.Create))
            {
                ValidIdFile.CopyTo(stream);
            }

            // ---- Create the appointment, pre-approved ----
            var newAppointment = new Appointment
            {
                ApplicantId = applicant.ApplicantId,
                Purpose = "Walk-in Document Request",   // Appointment.Purpose is NOT NULL in the DB
                Email = Email,
                ContactNumber = ContactNumber,
                AppointmentDate = parsedDate,
                AppointmentTime = parsedTime,
                Status = "Approved",
                DateRequested = DateTime.Now,
                DateApproved = DateTime.Now,
                AdditionalNotes = AdditionalNotes,
                ResumeFile = "/Uploads/Resumes/" + resumeFileName,
                ValidIDFile = "/Uploads/ValidIDs/" + validIdFileName
            };

            _context.Appointments.Add(newAppointment);
            _context.SaveChanges();

            TempData["Success"] = "Request created and approved successfully!";
            return RedirectToAction("RequestManagement");
        }

        public ActionResult AuditLogs()
        {
            if (HttpContext.Session.GetInt32("AdminId") == null)
                return RedirectToAction("Login");

            return View();
        }

        // ================== USER MANAGEMENT: ADD NEW USER ==================

        [HttpGet]
        public ActionResult AddNewUser()
        {
            if (HttpContext.Session.GetInt32("AdminId") == null)
                return RedirectToAction("Login");

            return View();
        }

        [HttpPost]
        public JsonResult AddNewUser(CreateUserRequest request)
        {
            if (HttpContext.Session.GetInt32("AdminId") == null)
                return Json(new { success = false, message = "Session expired. Please log in again." });

            if (string.IsNullOrWhiteSpace(request.FullName) ||
                string.IsNullOrWhiteSpace(request.ContactNumber) ||
                string.IsNullOrWhiteSpace(request.Username) ||
                string.IsNullOrWhiteSpace(request.Email) ||
                string.IsNullOrWhiteSpace(request.Password) ||
                string.IsNullOrWhiteSpace(request.Role))
            {
                return Json(new { success = false, message = "Please fill in all required fields." });
            }

            if (request.Password != request.ConfirmPassword)
            {
                return Json(new { success = false, message = "Passwords do not match." });
            }

            bool usernameTaken =
                _context.Admins.Any(a => a.Username == request.Username) ||
                _context.Staffs.Any(s => s.Username == request.Username);

            bool emailTaken =
                _context.Admins.Any(a => a.Email == request.Email) ||
                _context.Staffs.Any(s => s.Email == request.Email);

            if (usernameTaken || emailTaken)
            {
                return Json(new { success = false, message = "Username or Email is already registered." });
            }

            string? savedFileName = null;

            if (request.ProfileImage != null && request.ProfileImage.Length > 0)
            {
                savedFileName = Guid.NewGuid().ToString() + Path.GetExtension(request.ProfileImage.FileName);
                string uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Images");

                if (!Directory.Exists(uploadsFolder))
                    Directory.CreateDirectory(uploadsFolder);

                string fullPath = Path.Combine(uploadsFolder, savedFileName);

                using (var stream = new FileStream(fullPath, FileMode.Create))
                {
                    request.ProfileImage.CopyTo(stream);
                }
            }

            string hashedPassword = HashPassword(request.Password);

            try
            {
                if (request.Role == "Admin")
                {
                    var newAdmin = new Admin
                    {
                        FullName = request.FullName,
                        ContactNumber = request.ContactNumber,
                        Username = request.Username,
                        Email = request.Email,
                        Password = hashedPassword,
                        ProfileImage = savedFileName,
                        DateCreated = DateTime.Now
                    };

                    _context.Admins.Add(newAdmin);
                }
                else if (request.Role == "Staff")
                {
                    var newStaff = new Staff
                    {
                        FullName = request.FullName,
                        ContactNumber = request.ContactNumber,
                        Username = request.Username,
                        Email = request.Email,
                        Password = hashedPassword,
                        ProfileImage = savedFileName,
                        DateCreated = DateTime.Now
                    };

                    _context.Staffs.Add(newStaff);
                }
                else
                {
                    return Json(new { success = false, message = "Invalid role selected." });
                }

                _context.SaveChanges();

                return Json(new { success = true, message = $"{request.Role} account created successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Something went wrong: " + ex.Message });
            }
        }

        // Register(Staff) moved to StaffController — this is where staff accounts
        // are self-registered, so it belongs with StaffLogin, not here.

        // TestHash() removed — it was an unauthenticated hash-oracle endpoint.
        // To verify a seed password's hash, run HashPassword locally in a
        // scratch console app instead of exposing it over HTTP.

        private string HashPassword(string password)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
                StringBuilder builder = new StringBuilder();
                foreach (byte b in bytes)
                {
                    builder.Append(b.ToString("x2"));
                }
                return builder.ToString();
            }
        }
    }
}