using Microsoft.AspNetCore.Mvc;
using SmartOfficeRecords.Data;
using SmartOfficeRecords.Models;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System;
using System.Collections.Generic;


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

            // These numbers feed your 5 summary cards (Total, Pending, Approved, Completed, Rejected)
            ViewBag.TotalRequests = _context.Appointments.Count();
            ViewBag.Pending = _context.Appointments.Count(a => a.Status == "Pending");
            ViewBag.Approved = _context.Appointments.Count(a => a.Status == "Approved");
            ViewBag.Completed = _context.Appointments.Count(a => a.Status == "Completed");
            ViewBag.Rejected = _context.Appointments.Count(a => a.Status == "Rejected");

            return View();
        }

        // Called by the dashboard's JavaScript to load chart data for the selected range
        [HttpGet]
        public JsonResult GetRequestChartData(string range)
        {
            if (HttpContext.Session.GetInt32("AdminId") == null)
                return Json(new { error = "Not logged in" });

            var now = DateTime.Now;
            List<string> labels = new List<string>();
            List<int> counts = new List<int>();

            if (range == "week")
            {
                // Monday to Sunday of the current week
                var startOfWeek = now.AddDays(-(int)now.DayOfWeek + (now.DayOfWeek == DayOfWeek.Sunday ? -6 : 1));

                for (int i = 0; i < 7; i++)
                {
                    var day = startOfWeek.AddDays(i);
                    int count = _context.Appointments.Count(a => a.DateRequested.Date == day.Date);

                    labels.Add(day.ToString("ddd")); // Mon, Tue, Wed...
                    counts.Add(count);
                }
            }
            else if (range == "month")
            {
                // Every day of the current month
                int daysInMonth = DateTime.DaysInMonth(now.Year, now.Month);

                for (int day = 1; day <= daysInMonth; day++)
                {
                    var date = new DateTime(now.Year, now.Month, day);
                    int count = _context.Appointments.Count(a => a.DateRequested.Date == date.Date);

                    labels.Add(day.ToString());
                    counts.Add(count);
                }
            }
            else if (range == "year")
            {
                // Every month of the current year
                for (int month = 1; month <= 12; month++)
                {
                    int count = _context.Appointments.Count(a =>
                        a.DateRequested.Year == now.Year && a.DateRequested.Month == month);

                    labels.Add(new DateTime(now.Year, month, 1).ToString("MMM")); // Jan, Feb...
                    counts.Add(count);
                }
            }

            return Json(new { labels, counts });
        }

        public ActionResult RequestManagement()
        {
            if (HttpContext.Session.GetInt32("AdminId") == null)
                return RedirectToAction("Login");

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
        public ActionResult RCreateRequest()
        {
            if (HttpContext.Session.GetInt32("AdminId") == null) 
                return RedirectToAction("Login");

            return View();
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