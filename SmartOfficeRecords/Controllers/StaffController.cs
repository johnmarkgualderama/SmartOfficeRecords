using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartOfficeRecords.Data;
using SmartOfficeRecords.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SmartOfficeRecords.Controllers
{
    public class StaffController : Controller
    {
        private readonly ApplicationDbContext _context;

        public StaffController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Staff Login Page
        [HttpGet]
        public ActionResult StaffLogin()
        {
            return View();
        }

        // POST: Staff Login
        [HttpPost]
        public IActionResult StaffLogin(string Username, string Password)
        {
            if (Username == "staff" && Password == "123")
            {
                return RedirectToAction("StaffDashboard", "Staff");
            }

            ViewBag.Error = "Invalid Username or Password";
            return View();
        }

        public IActionResult StaffDashboard()
        {
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

            var approvedTodayOrdered = todaysAppointmentsRaw
                .Where(a => a.DateApproved != null && a.DateApproved.Value.Date == today)
                .OrderBy(a => a.DateApproved)
                .ToList();

            var approvalDisplayIdMap = new Dictionary<int, string>();
            for (int i = 0; i < approvedTodayOrdered.Count; i++)
            {
                approvalDisplayIdMap[approvedTodayOrdered[i].AppointmentId] = "AP" + (i + 1).ToString("D3");
            }

            ViewBag.RecentApplicants = todaysAppointmentsRaw
                .Where(a => a.Status == "Approved" || a.Status == "Completed")
                .OrderBy(a => a.DateApproved ?? a.DateRequested)
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

        // Staff-side copy of Admin's chart endpoint — no AdminId session check,
        // since Staff doesn't have one. Keeps the "This Week/Month/Year" graph working.
        [HttpGet]
        public JsonResult GetRequestChartData(string range, int? year)
        {
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

        public IActionResult StaffRecordsManagement()
        {
            return View();
        }

        // GET: Admin/RecordsManagement
        public ActionResult UploadFiles()
        {
            return View();
        }

        public ActionResult UploadDetails()
        {
            return View();
        }

        public ActionResult UploadReviewConfirm()
        {
            return View();
        }

        // GET: Staff/StaffRequest
        public ActionResult StaffRequest(string search, string status, int page = 1)
        {
            const int pageSize = 5;
            var today = DateTime.Today;
            bool isSearching = !string.IsNullOrWhiteSpace(search);

            // ----- DAILY-RESET DISPLAY IDS (ACROSS ALL HISTORY) -----
            var allForNumbering = _context.Appointments
                .OrderBy(a => a.DateRequested)
                .Select(a => new { a.AppointmentId, a.DateRequested })
                .ToList();

            var displayIdMap = new Dictionary<int, string>();
            var dailyCounters = new Dictionary<DateTime, int>();

            foreach (var item in allForNumbering)
            {
                var day = item.DateRequested.Date;

                if (!dailyCounters.ContainsKey(day))
                    dailyCounters[day] = 0;

                dailyCounters[day]++;
                displayIdMap[item.AppointmentId] = "AP" + dailyCounters[day].ToString("D3");
            }

            // ----- BASE QUERY -----
            var query = _context.Appointments
                .Include(a => a.Applicant)
                .AsQueryable();

            if (!isSearching)
            {
                query = query.Where(a => a.DateRequested.Date == today);
            }

            if (isSearching)
            {
                query = query.Where(a =>
                    a.Applicant!.FullName.Contains(search) ||
                    a.Applicant!.Email.Contains(search));
            }

            if (!string.IsNullOrWhiteSpace(status) && status != "All Status")
            {
                query = query.Where(a => a.Status == status);
            }

            // ----- PAGINATION -----
            int totalCount = query.Count();
            int totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
            if (totalPages < 1) totalPages = 1;
            if (page < 1) page = 1;
            if (page > totalPages) page = totalPages;

            var requests = query
                .OrderBy(a => a.Status == "Pending" ? 0 : 1)
                .ThenBy(a => a.DateRequested)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(a => new
                {
                    a.AppointmentId,
                    ApplicantName = a.Applicant!.FullName,
                    ApplicantEmail = a.Applicant!.Email,
                    DateAppliedFormatted = a.DateRequested.ToString("MMM dd, yyyy"),
                    a.Status,
                    a.ContactNumber,
                    a.Email,
                    a.AppointmentDate,
                    a.AppointmentTime,
                    a.DateRequested,
                    a.AdditionalNotes,
                    a.ResumeFile,
                    a.ValidIDFile
                })
                .ToList()
                .Select(a => new
                {
                    a.AppointmentId,
                    a.ApplicantName,
                    a.ApplicantEmail,
                    a.DateAppliedFormatted,
                    a.Status,
                    DisplayId = displayIdMap.ContainsKey(a.AppointmentId) ? displayIdMap[a.AppointmentId] : "AP000",
                    Initials = GetInitials(a.ApplicantName),
                    ContactNumber = string.IsNullOrWhiteSpace(a.ContactNumber) ? "Not provided" : a.ContactNumber,
                    Email = string.IsNullOrWhiteSpace(a.Email) ? a.ApplicantEmail : a.Email,
                    AppointmentDateFormatted = a.AppointmentDate.ToString("MM/dd/yyyy"),
                    AppointmentTimeFormatted = DateTime.Today.Add(a.AppointmentTime).ToString("hh:mm tt"),
                    DateSubmittedFormatted = a.DateRequested.ToString("MM/dd/yyyy . hh:mm tt"),
                    AdditionalNotes = string.IsNullOrWhiteSpace(a.AdditionalNotes) ? "No additional notes provided." : a.AdditionalNotes,
                    a.ResumeFile,
                    a.ValidIDFile
                })
                .ToList();

            ViewBag.Requests = requests;
            ViewBag.SearchTerm = search;
            ViewBag.SelectedStatus = status;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalCount = totalCount;
            ViewBag.PageSize = pageSize;

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
    }
}