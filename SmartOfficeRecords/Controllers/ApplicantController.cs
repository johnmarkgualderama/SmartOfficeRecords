using Microsoft.AspNetCore.Mvc;
using SmartOfficeRecords.Data;
using SmartOfficeRecords.Models;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using SmartOfficeRecords.Services;


namespace SmartOfficeRecords.Controllers
{
    public class ApplicantController : Controller
    {

        private readonly EmailService _emailService;
        private readonly ApplicationDbContext _context;

        //ApplicantLogin
        [HttpGet]
        public ActionResult ApplicantLogin()
        {
            return View();
        }

        //ApplicantLogin
        [HttpPost]
        public ActionResult ApplicantLogin(string Username, string Password)
        {
            // Basic validation first
            if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
            {
                ViewBag.Error = "Please enter both Username and Password.";
                return View();
            }

            // Hash the entered password the SAME way we hashed it during registration,
            // so we can compare hash-to-hash (we never store or compare plain text).
            string hashedPassword = HashPassword(Password);

            // Look for a matching applicant in the database
            var applicant = _context.ApplicantRegisters
                .FirstOrDefault(a => a.Username == Username && a.Password == hashedPassword);

            if (applicant == null)
            {
                ViewBag.Error = "Invalid Username or Password";
                return View();
            }

            // ----- LOGIN SUCCESS -----
            HttpContext.Session.SetInt32("ApplicantId", applicant.ApplicantId);
            HttpContext.Session.SetString("ApplicantName", applicant.FullName);
            HttpContext.Session.SetString("ApplicantUsername", applicant.Username);

            return RedirectToAction("ApplicantDash");
        }

        //ApplicantRegister
        public ActionResult ApplicantRegister()
        {
            return View();
        }

        [HttpPost]
        public ActionResult ApplicantRegister(
            string Fullname,
            string Username,
            string Email,
            string ContactNumber,
            string Birthdate,
            string InvitedBy,
            string Password,
            string ConfirmPassword,
            IFormFile ProfileImage)
        {
            // ----- STEP 1: BASIC VALIDATION -----

            if (string.IsNullOrWhiteSpace(Fullname) ||
                string.IsNullOrWhiteSpace(Username) ||
                string.IsNullOrWhiteSpace(Email) ||
                string.IsNullOrWhiteSpace(ContactNumber) ||
                string.IsNullOrWhiteSpace(Birthdate) ||
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

            if (!DateTime.TryParse(Birthdate, out DateTime parsedBirthdate))
            {
                ViewBag.Error = "Invalid birthdate.";
                return View();
            }

            // ----- STEP 2: CHECK FOR DUPLICATE USERNAME / EMAIL -----

            bool alreadyExists = _context.ApplicantRegisters
                .Any(a => a.Username == Username || a.Email == Email);

            if (alreadyExists)
            {
                ViewBag.Error = "Username or Email is already registered.";
                return View();
            }

            // ----- STEP 3: SAVE THE PROFILE IMAGE -----

            string savedFileName = null;

            if (ProfileImage != null && ProfileImage.Length > 0)
            {
                // Give the file a unique name so uploads never overwrite each other
                savedFileName = Guid.NewGuid().ToString() + Path.GetExtension(ProfileImage.FileName);

                string uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Images");

                // Create the folder if it doesn't exist yet
                if (!Directory.Exists(uploadsFolder))
                {
                    Directory.CreateDirectory(uploadsFolder);
                }

                string fullPath = Path.Combine(uploadsFolder, savedFileName);

                using (var stream = new FileStream(fullPath, FileMode.Create))
                {
                    ProfileImage.CopyTo(stream);
                }
            }

            // ----- STEP 4: HASH THE PASSWORD -----

            string hashedPassword = HashPassword(Password);

            // ----- STEP 5: BUILD THE MODEL AND SAVE TO DATABASE -----

            var newApplicant = new ApplicantRegister
            {
                FullName = Fullname,
                Username = Username,
                Email = Email,
                ContactNumber = ContactNumber,
                Birthdate = parsedBirthdate,
                InvitedBy = InvitedBy,
                Password = hashedPassword,
                ProfileImage = savedFileName
            };

            try
            {
                _context.ApplicantRegisters.Add(newApplicant); // stage the insert
                _context.SaveChanges();                        // actually run the SQL INSERT

                ViewBag.Success = "Registered Successfully!";
            }
            catch (Exception ex)
            {
                ViewBag.Error = "Something went wrong while saving: " + ex.Message;
            }

            return View();
        }

        // Simple, dependency-free password hashing using SHA256.
        // (For production-grade security, consider BCrypt.Net-Next instead —
        // it's slower on purpose, which makes brute-forcing harder.)
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
        public ActionResult ApplicantLand()
        {
            // If there's no ApplicantId in session, they never logged in — send them back
            if (HttpContext.Session.GetInt32("ApplicantId") == null)
            {
                return RedirectToAction("ApplicantLogin");
            }

            return View();
        }

        public ActionResult ApplicantDash()
        {
            if (HttpContext.Session.GetInt32("ApplicantId") == null)
            {
                return RedirectToAction("ApplicantLogin");
            }

            return View();
        }

        public ActionResult Profile()
        {
            int? applicantId = HttpContext.Session.GetInt32("ApplicantId");
            if (applicantId == null)
            {
                return RedirectToAction("ApplicantLogin");
            }

            // Fetch this applicant's actual data from the database to show on the profile page
            var applicant = _context.ApplicantRegisters.Find(applicantId);
            return View(applicant);
        }
        public ActionResult ApplicantLogout()
        {
            HttpContext.Session.Clear(); // wipes everything stored in session
            return RedirectToAction("ApplicantLogin");
        }



        public ApplicantController(ApplicationDbContext context, EmailService emailService)
        {
            _context = context;
            _emailService = emailService;
        }


        // ================== FORGOT PASSWORD - STEP 1: Request a code ==================

        [HttpGet]
        public ActionResult ForgotPassword()
        {
            return View();
        }

        [HttpPost]
        public ActionResult ForgotPassword(string Email)
        {
            if (string.IsNullOrWhiteSpace(Email))
            {
                ViewBag.Error = "Please enter your email.";
                return View();
            }

            var applicant = _context.ApplicantRegisters.FirstOrDefault(a => a.Email == Email);

            if (applicant == null)
            {
                // Don't reveal whether the email exists or not — just show a generic message.
                ViewBag.Error = "If this email is registered, a code has been sent.";
                return View();
            }

            // Generate a random 6-digit code, e.g. "482913"
            string code = new Random().Next(100000, 999999).ToString();

            applicant.ResetCode = code;
            applicant.ResetCodeExpiry = DateTime.Now.AddMinutes(5); // code expires in 5 minutes

            _context.SaveChanges();

            // Send the code by email
            string subject = "Your Password Reset Code";
            string body = $@"
                    <p>Hello {applicant.FullName},</p>
                    <p>Your password reset code is:</p>
                    <h2>{code}</h2>
                    <p>This code will expire in 5 minutes. If you didn't request this, you can ignore this email.</p>";

            _emailService.SendEmail(applicant.Email, subject, body);

            // Remember which email we're resetting for, so the next steps know who this is
            TempData["ResetEmail"] = applicant.Email;

            return RedirectToAction("VerifyResetCode");
        }


        // ================== FORGOT PASSWORD - STEP 2: Enter the code ==================

        [HttpGet]
        public ActionResult VerifyResetCode()
        {
            if (TempData["ResetEmail"] == null)
            {
                return RedirectToAction("ForgotPassword");
            }

            // Keep it alive for the POST that follows
            TempData.Keep("ResetEmail");

            return View();
        }

        [HttpPost]
        public ActionResult VerifyResetCode(string Code)
        {
            string email = TempData["ResetEmail"] as string;

            if (email == null)
            {
                return RedirectToAction("ForgotPassword");
            }

            var applicant = _context.ApplicantRegisters.FirstOrDefault(a => a.Email == email);

            if (applicant == null || applicant.ResetCode != Code || applicant.ResetCodeExpiry < DateTime.Now)
            {
                ViewBag.Error = "Invalid or expired code.";
                TempData["ResetEmail"] = email; // keep the flow alive so they can retry
                TempData.Keep("ResetEmail");
                return View();
            }

            // Code is correct — move to the final step
            TempData["ResetEmail"] = email;
            TempData.Keep("ResetEmail");

            return RedirectToAction("ResetPassword");
        }


        // ================== FORGOT PASSWORD - STEP 3: Set new password ==================

        [HttpGet]
        public ActionResult ResetPassword()
        {
            if (TempData["ResetEmail"] == null)
            {
                return RedirectToAction("ForgotPassword");
            }

            TempData.Keep("ResetEmail");
            return View();
        }

        [HttpPost]
        public ActionResult ResetPassword(string NewPassword, string ConfirmPassword)
        {
            string email = TempData["ResetEmail"] as string;

            if (email == null)
            {
                return RedirectToAction("ForgotPassword");
            }

            if (string.IsNullOrWhiteSpace(NewPassword) || NewPassword != ConfirmPassword)
            {
                ViewBag.Error = "Passwords do not match.";
                TempData["ResetEmail"] = email;
                TempData.Keep("ResetEmail");
                return View();
            }

            var applicant = _context.ApplicantRegisters.FirstOrDefault(a => a.Email == email);

            if (applicant == null)
            {
                return RedirectToAction("ForgotPassword");
            }

            // Save the new hashed password
            applicant.Password = HashPassword(NewPassword);

            // Clear the reset code so it can't be reused
            applicant.ResetCode = null;
            applicant.ResetCodeExpiry = null;

            _context.SaveChanges();

            ViewBag.Success = "Password reset successfully! Please log in.";
            return RedirectToAction("ApplicantLogin");
        }
        // GET: Applicant/MyAppointment
        public ActionResult MyAppointment()
        {
            int? applicantId = HttpContext.Session.GetInt32("ApplicantId");
            if (applicantId == null)
                return RedirectToAction("ApplicantLogin");

            var myAppointments = _context.Appointments
                .Where(a => a.ApplicantId == applicantId)
                .OrderByDescending(a => a.DateRequested)
                .ToList();

            return View(myAppointments);
        }

        // POST: Applicant/BookAppointment
        [HttpPost]
        public ActionResult BookAppointment(string Purpose, string AppointmentDate, string AppointmentTime)
        {
            int? applicantId = HttpContext.Session.GetInt32("ApplicantId");
            if (applicantId == null)
                return RedirectToAction("ApplicantLogin");

            if (string.IsNullOrWhiteSpace(Purpose) ||
                !DateTime.TryParse(AppointmentDate, out DateTime parsedDate) ||
                !TimeSpan.TryParse(AppointmentTime, out TimeSpan parsedTime))
            {
                ViewBag.Error = "Please fill in all appointment fields correctly.";
                return RedirectToAction("MyAppointment");
            }

            var newAppointment = new Appointment
            {
                ApplicantId = applicantId.Value,
                Purpose = Purpose,
                AppointmentDate = parsedDate,
                AppointmentTime = parsedTime,
                Status = "Pending",
                DateRequested = DateTime.Now // this is what feeds the admin dashboard graph
            };

            _context.Appointments.Add(newAppointment);
            _context.SaveChanges();

            ViewBag.Success = "Appointment request submitted!";
            return RedirectToAction("MyAppointment");
        }
    }            
}            