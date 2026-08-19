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
    public class AdminController : Controller
    {
        // EF Core gives us access to the database through this object.
        private readonly ApplicationDbContext _context;

        public AdminController(ApplicationDbContext context)
        {
            _context = context;
        }

        // ================== LOGIN ==================

        // GET: Admin/Login
        [HttpGet]
        public ActionResult Login()
        {
            return View();
        }

        // POST: Admin/Login
        [HttpPost]
        public ActionResult Login(string Username, string Password)
        {
            if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
            {
                ViewBag.Error = "Please enter both Username and Password.";
                return View();
            }

            // Hash whatever was typed, the SAME way we hashed it when we seeded the account
            string hashedPassword = HashPassword(Password);

              var admin = _context.Admins
                .FirstOrDefault(a => a.Username == Username && a.Password == hashedPassword);

            if (admin == null)
            {
                ViewBag.Error = "Invalid Username or Password";
                return View();
            }  

            // Store identifying info in session so other Admin pages know who's logged in
            HttpContext.Session.SetInt32("AdminId", admin.AdminId);
            HttpContext.Session.SetString("AdminName", admin.FullName);
            HttpContext.Session.SetString("AdminUsername", admin.Username);

            return RedirectToAction("Dashboard");
        }

        // GET: Admin/Logout
        public ActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Login");
        }


        // ================== DASHBOARD & PAGES ==================
        // Each of these checks the session first — if there's no AdminId,
        // the user never logged in, so we bounce them back to the login page.

        public IActionResult Dashboard()
        {
            if (HttpContext.Session.GetInt32("AdminId") == null)
                return RedirectToAction("Login");

            var today = DateTime.Today;

            // ----- TODAY'S APPOINTMENTS (single source of truth for cards, popups, and Recent Applicants) -----
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

            // ----- DAILY-RESET DISPLAY IDS (same scheme as Request Management) -----
            var displayIdMap = new Dictionary<int, string>();
            var orderedForNumbering = todaysAppointmentsRaw.OrderBy(a => a.DateRequested).ToList();
            for (int i = 0; i < orderedForNumbering.Count; i++)
            {
                displayIdMap[orderedForNumbering[i].AppointmentId] = "AP" + (i + 1).ToString("D3");
            }

            // Cycled colors for the avatar circles in the popup tables
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

            // ----- DAILY-RESET DISPLAY IDS FOR RECENT APPLICANTS (ordered by approval time) -----
            var approvedTodayOrdered = todaysAppointmentsRaw
                .Where(a => a.DateApproved != null && a.DateApproved.Value.Date == today)
                .OrderBy(a => a.DateApproved)
                .ToList();

            var approvalDisplayIdMap = new Dictionary<int, string>();
            for (int i = 0; i < approvedTodayOrdered.Count; i++)
            {
                approvalDisplayIdMap[approvedTodayOrdered[i].AppointmentId] = "AP" + (i + 1).ToString("D3");
            }

            // Recent Applicants table shows Approved and Completed, numbered by
            // approval order (not submission order) so whoever was approved first
            // today shows as AP001 regardless of their Request Management ID.
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

            // ----- YEAR FILTER OPTIONS (for the Request Overview graph) -----
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
        [HttpPost]
        public ActionResult ApproveAppointment(int AppointmentId)
        {
            if (HttpContext.Session.GetInt32("AdminId") == null)
                return RedirectToAction("Login");

            var appointment = _context.Appointments.Find(AppointmentId);
            if (appointment != null) 
            {
                appointment.Status = "Approved";
                appointment.DateApproved = DateTime.Now;   // <-- ADD THIS LINE
                _context.SaveChanges();
            }

            return RedirectToAction("Dashboard");
        }

        [HttpPost]
        public ActionResult CompleteAppointment(int AppointmentId)
        {
            if (HttpContext.Session.GetInt32("AdminId") == null)
                return RedirectToAction("Login");

            var appointment = _context.Appointments.Find(AppointmentId);
            if (appointment != null && appointment.Status == "Approved")
            {
                appointment.Status = "Completed";
                appointment.DateCompleted = DateTime.Now;   // <-- add this line
                _context.SaveChanges();
            }

            return RedirectToAction("Dashboard");
        }

        [HttpPost]
        public ActionResult RejectAppointment(int AppointmentId)
        {
            if (HttpContext.Session.GetInt32("AdminId") == null)
                return RedirectToAction("Login");

            var appointment = _context.Appointments.Find(AppointmentId);
            if (appointment != null)
            {
                appointment.Status = "Rejected";
                _context.SaveChanges();
            }

            return RedirectToAction("Dashboard");
        }

        // GET: Admin/ViewAppointment/5
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

        // Called by the dashboard's JavaScript to load chart data for the selected range
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
                // Week view only supports the current year — the frontend
                // automatically switches to Month view if a past year is picked.
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
                // Uses the current month number, but within whichever year was selected
                // (e.g. viewing "August 2026" even if today is in a later year/month)
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


        public ActionResult RequestManagement(string search, string status, int page = 1)
        {
            if (HttpContext.Session.GetInt32("AdminId") == null)
                return RedirectToAction("Login");

            const int pageSize = 5;
            var today = DateTime.Today;
            bool isSearching = !string.IsNullOrWhiteSpace(search);

            // ----- DAILY-RESET DISPLAY IDS (ACROSS ALL HISTORY) -----
            // Every appointment gets an ID based on its OWN day's sequence, so a
            // request from July 19 shows as AP001 (if it was the first that day),
            // and today's requests show AP001, AP002... independently. This runs
            // across all appointments so search results (which can span any date)
            // still show the correct per-day ID.
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

            // Only restrict to today when the admin isn't actively searching.
            // Searching (by name/email) or nothing typed but a status filter
            // still applied on its own doesn't count as "searching" here —
            // only a non-empty search box lifts the today-only restriction.
            if (!isSearching)
            {
                query = query.Where(a => a.DateRequested.Date == today);
            }

            // ----- SEARCH FILTER -----
            if (isSearching)
            {
                query = query.Where(a =>
                    a.Applicant!.FullName.Contains(search) ||
                    a.Applicant!.Email.Contains(search));
            }

            // ----- STATUS FILTER -----
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

        public ActionResult RecordsManagement()
        {
            if (HttpContext.Session.GetInt32("AdminId") == null)
                return RedirectToAction("Login");

            return View();
        }

        public ActionResult ReportsManagement()
        {
            if (HttpContext.Session.GetInt32("AdminId") == null)
                return RedirectToAction("Login");

            return View();
        }

        public ActionResult UsersManagement()
        {
            if (HttpContext.Session.GetInt32("AdminId") == null)
                return RedirectToAction("Login");

            return View();
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

            // Pull the ACTUAL logged-in admin's info instead of hardcoded values
            int adminId = HttpContext.Session.GetInt32("AdminId")!.Value;
            var adminRecord = _context.Admins.Find(adminId);

            var admin = new AdminViewModel
            {
                FullName = adminRecord?.FullName ?? "Unknown",
                Username = adminRecord?.Username ?? "Unknown",
                Email = "admin@sors.com",   // update this if/when Admin model gets an Email field
                Phone = "09123456789"       // update this if/when Admin model gets a Phone field
            };

            return View(admin);
        }

        // GET: Admin/RequestManagement/CreateRequest
        [HttpGet]
        public ActionResult RCreateRequest()
        {
            if (HttpContext.Session.GetInt32("AdminId") == null)
                return RedirectToAction("Login");

            return View();
        }

        [HttpPost]
        public ActionResult RCreateRequest(string PositionType, string RequestType, string Title, string PreferredDate, string Description)
        {
            if (HttpContext.Session.GetInt32("AdminId") == null)
                return RedirectToAction("Login");

            if (string.IsNullOrWhiteSpace(Title) || string.IsNullOrWhiteSpace(PreferredDate))
            {
                TempData["Error"] = "Please fill in all required fields.";
                return RedirectToAction("RCreateRequest");
            }

            if (!DateTime.TryParse(PreferredDate, out DateTime parsedDate))
            {
                TempData["Error"] = "Invalid date.";
                return RedirectToAction("RCreateRequest");
            }

            var newRequest = new Appointment
            {
                Purpose = Title,
                AppointmentDate = parsedDate,
                Status = "Pending",
                DateRequested = DateTime.Now,
                AdditionalNotes = Description
            };

            _context.Appointments.Add(newRequest);
            _context.SaveChanges();

            TempData["Success"] = "Request created successfully!";
            return RedirectToAction("RequestManagement");
        }

        // GET: Admin/UserManagement/AddNewUser
        public ActionResult AddNewUser()
        {
            if (HttpContext.Session.GetInt32("AdminId") == null)
                return RedirectToAction("Login");

            return View();
        }


        // ================== REGISTER (Staff, presumably) ==================

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
            // PASSWORD CHECK
            if (Password != ConfirmPassword)
            {
                ViewBag.Error = "Password does not match.";
                return View();
            }

            // CHECK IMAGE
            if (ProfileImage != null)
            {
                string fileName = System.IO.Path.GetFileName(ProfileImage.FileName);

                string path = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/Uploads", fileName);

                using (var stream = new FileStream(path, FileMode.Create))
                {
                    ProfileImage.CopyTo(stream);
                }
            }

            // NOTE: this currently does NOT save anything to the database.
            // It just saves the image and shows a success message.
            // Let me know if this Register form is meant to create Staff accounts —
            // if so, we'll wire it up to a Staff table the same way Applicant register works.

            ViewBag.Success = "Registered Successfully!";

            return View();
        }


        // Same hashing method as ApplicantController — MUST produce identical
        // output for the same password, so keep this logic exactly matching.
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
        public ActionResult TestHash()
        {
            string hash = HashPassword("admin0000"); // whatever password you want
            return Content(hash);
        }
    }
}