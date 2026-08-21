// ================= StaffController.cs =================
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartOfficeRecords.Data;
using SmartOfficeRecords.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SmartOfficeRecords.Controllers
{
    public class StaffController : Controller
    {
        private readonly ApplicationDbContext _context;

        public StaffController(ApplicationDbContext context)
        {
            _context = context;
        }

        // ================== LOGIN ==================

        [HttpGet]
        public ActionResult StaffLogin()
        {
            return View();
        }

        [HttpPost]
        public IActionResult StaffLogin(string Username, string Password)
        {
            if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
            {
                ViewBag.Error = "Please enter both Username and Password.";
                return View();
            }

            string hashedPassword = HashPassword(Password);

            var staff = _context.Staffs
                .FirstOrDefault(s => s.Username == Username && s.Password == hashedPassword);

            if (staff == null)
            {
                ViewBag.Error = "Invalid Username or Password";
                return View();
            }

            if (!staff.IsActive)
            {
                ViewBag.Error = "This account has been deactivated. Contact an administrator.";
                return View();
            }
              
            staff.LastLogin = DateTime.Now;
            _context.SaveChanges();

            HttpContext.Session.SetInt32("StaffId", staff.StaffId);
            HttpContext.Session.SetString("StaffName", staff.FullName);
            HttpContext.Session.SetString("StaffUsername", staff.Username);

            return RedirectToAction("StaffDashboard");
        }

        public ActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("StaffLogin");
        }

        // ================== REGISTER ==================

        [HttpGet]
        public ActionResult Register()
        {
            return View();
        }

        [HttpPost]
        public ActionResult Register(
            string Fullname,
            string Username,
            string Email,
            string Contact,
            string Address,
            string Department,
            string Password,
            string ConfirmPassword,
            IFormFile ProfileImage)
        {
            if (string.IsNullOrWhiteSpace(Fullname) ||
                string.IsNullOrWhiteSpace(Username) ||
                string.IsNullOrWhiteSpace(Email) ||
                string.IsNullOrWhiteSpace(Password))
            {
                ViewBag.Error = "Please fill in all required fields.";
                return View();
            }

            if (Password != ConfirmPassword)
            {
                ViewBag.Error = "Password does not match.";
                return View();
            }

            bool usernameTaken =
                _context.Admins.Any(a => a.Username == Username) ||
                _context.Staffs.Any(s => s.Username == Username);

            bool emailTaken =
                _context.Admins.Any(a => a.Email == Email) ||
                _context.Staffs.Any(s => s.Email == Email);

            if (usernameTaken || emailTaken)
            {
                ViewBag.Error = "Username or Email is already registered.";
                return View();
            }

            string? savedFileName = null;

            if (ProfileImage != null && ProfileImage.Length > 0)
            {
                savedFileName = Guid.NewGuid().ToString() + Path.GetExtension(ProfileImage.FileName);
                string uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Images");

                if (!Directory.Exists(uploadsFolder))
                    Directory.CreateDirectory(uploadsFolder);

                string fullPath = Path.Combine(uploadsFolder, savedFileName);

                using (var stream = new FileStream(fullPath, FileMode.Create))
                {
                    ProfileImage.CopyTo(stream);
                }
            }

            // NOTE: Address and Department are collected here but the Staff
            // table has no columns for them yet, so they are not persisted.
            // Run: ALTER TABLE Staff ADD Address VARCHAR(255) NULL, Department VARCHAR(100) NULL;
            // then add matching properties to the Staff model if you want to keep them.

            var newStaff = new Staff
            {
                FullName = Fullname,
                Username = Username,
                Email = Email,
                ContactNumber = Contact,
                Password = HashPassword(Password),
                ProfileImage = savedFileName,
                DateCreated = DateTime.Now
            };

            _context.Staffs.Add(newStaff);
            _context.SaveChanges();

            ViewBag.Success = "Registered Successfully!";
            return View();
        }

        // ================== DASHBOARD & PAGES ==================

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

        public ActionResult StaffRequest(string search, string status, int page = 1)
        {
            const int pageSize = 5;
            var today = DateTime.Today;
            bool isSearching = !string.IsNullOrWhiteSpace(search);

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