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

        public ApplicantController(ApplicationDbContext context, EmailService emailService)
        {
            _context = context;
            _emailService = emailService;
        }

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

                // Log the applicant in immediately after registering
                HttpContext.Session.SetInt32("ApplicantId", newApplicant.ApplicantId);
                HttpContext.Session.SetString("ApplicantName", newApplicant.FullName);
                HttpContext.Session.SetString("ApplicantUsername", newApplicant.Username);

                TempData["Success"] = "Registered Successfully!";
                return RedirectToAction("ApplicantDash");
            }
            catch (Exception ex)
            {
                ViewBag.Error = "Something went wrong while saving: " + ex.Message;
                return View();
            }
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
            return View();
        }

        public ActionResult ApplicantDash()
        {
            if (!LoadLoggedInApplicant())
                return RedirectToAction("ApplicantLogin");

            ViewBag.Success = TempData["Success"];

            return View();
        }

        public ActionResult SomeAction()
        {
            if (!LoadLoggedInApplicant())
                return RedirectToAction("ApplicantLogin");

            return View();
        }

        // GET: Applicant/Profile
        public ActionResult Profile()
        {
            if (!LoadLoggedInApplicant())
                return RedirectToAction("ApplicantLogin");

            int applicantId = HttpContext.Session.GetInt32("ApplicantId")!.Value;
            var applicant = _context.ApplicantRegisters.Find(applicantId);

            return View(applicant);
        }

        // POST: Applicant/Profile
        [HttpPost]
        public ActionResult Profile(string Username, string Email, string ContactNumber)
        {
            if (!LoadLoggedInApplicant())
                return RedirectToAction("ApplicantLogin");

            int applicantId = HttpContext.Session.GetInt32("ApplicantId")!.Value;
            var applicant = _context.ApplicantRegisters.Find(applicantId);

            if (applicant == null)
                return RedirectToAction("ApplicantLogin");

            if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(ContactNumber))
            {
                ViewBag.Error = "Please fill in all fields.";
                return View(applicant);
            }

            bool taken = _context.ApplicantRegisters
                .Any(a => a.ApplicantId != applicantId && (a.Username == Username || a.Email == Email));

            if (taken)
            {
                ViewBag.Error = "That Username or Email is already in use by another account.";
                return View(applicant);
            }

            applicant.Username = Username;
            applicant.Email = Email;
            applicant.ContactNumber = ContactNumber;

            _context.SaveChanges();

            HttpContext.Session.SetString("ApplicantUsername", applicant.Username);

            ViewBag.Success = "Profile updated successfully!";

            // Refresh ViewBag with the updated info, using the SAME logic as LoadLoggedInApplicant()
            LoadLoggedInApplicant();

            return View(applicant);
        }

        public ActionResult ApplicantLogout()
        {
            HttpContext.Session.Clear(); // wipes everything stored in session
            return RedirectToAction("ApplicantLogin");
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
            if (!LoadLoggedInApplicant())
                return RedirectToAction("ApplicantLogin");

            int applicantId = HttpContext.Session.GetInt32("ApplicantId")!.Value;

            var myAppointments = _context.Appointments
                .Where(a => a.ApplicantId == applicantId)
                .OrderBy(a => a.DateRequested)
                .ToList();

            ViewBag.Success = TempData["Success"];
            ViewBag.CancelSuccess = TempData["CancelSuccess"] != null;

            return View(myAppointments);
        }

        private bool LoadLoggedInApplicant()
        {
            int? applicantId = HttpContext.Session.GetInt32("ApplicantId");
            if (applicantId == null)
                return false;

            var applicant = _context.ApplicantRegisters.Find(applicantId);
            if (applicant == null)
                return false;

            string cleanName = applicant.FullName.Trim();
            string[] nameParts = cleanName.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            string initials;
            if (nameParts.Length >= 2)
            {
                initials = $"{nameParts[0][0]}{nameParts[^1][0]}".ToUpper();
            }
            else if (nameParts.Length == 1 && nameParts[0].Length >= 2)
            {
                initials = nameParts[0].Substring(0, 2).ToUpper();
            }
            else if (nameParts.Length == 1)
            {
                initials = nameParts[0].ToUpper();
            }
            else
            {
                initials = "?";
            }

            ViewBag.FullName = applicant.FullName;
            ViewBag.Username = applicant.Username;
            ViewBag.Email = applicant.Email;
            ViewBag.Initials = initials;

            return true;
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

            var applicant = _context.ApplicantRegisters.Find(applicantId);

            if (applicant == null)
            {
                return RedirectToAction("ApplicantLogin");
            }

            // Get initials safely, regardless of whether FullName has spaces or not
            string cleanName = applicant.FullName.Trim();
            string initials;

            if (cleanName.Length >= 2)
            {
                initials = cleanName.Substring(0, 2).ToUpper(); // first 2 letters, e.g. "Jo" -> "JO"
            }
            else if (cleanName.Length == 1)
            {
                initials = cleanName.ToUpper();
            }
            else
            {
                initials = "?";
            }

            ViewBag.FullName = applicant.FullName;
            ViewBag.Username = applicant.Username;
            ViewBag.Email = applicant.Email;
            ViewBag.Initials = initials;

            _context.Appointments.Add(newAppointment);
            _context.SaveChanges();

            ViewBag.Success = "Appointment request submitted!";
            return RedirectToAction("MyAppointment");
        }

        // GET: Applicant/BookInterview
        public ActionResult BookInterview()
        {
            if (!LoadLoggedInApplicant())
                return RedirectToAction("ApplicantLogin");

            int applicantId = HttpContext.Session.GetInt32("ApplicantId")!.Value;

            // Find the applicant's most recent Completed interview
            var lastCompleted = _context.Appointments
                .Where(a => a.ApplicantId == applicantId && a.Status == "Completed" && a.DateCompleted != null)
                .OrderByDescending(a => a.DateCompleted)
                .FirstOrDefault();

            if (lastCompleted != null)
            {
                DateTime completedDate = lastCompleted.DateCompleted!.Value;
                DateTime eligibleDate = completedDate.AddDays(30);
                int daysLeft = (eligibleDate.Date - DateTime.Today).Days;

                if (daysLeft > 0)
                {
                    // Still in the 30-day cooldown — block new bookings
                    ViewBag.InCooldown = true;
                    ViewBag.DaysLeft = daysLeft;
                    ViewBag.EligibleDate = eligibleDate.ToString("MMMM dd, yyyy");
                    ViewBag.LastInterviewDate = lastCompleted.AppointmentDate.ToString("MMMM dd, yyyy");
                    ViewBag.PositionApplied = lastCompleted.Purpose;
                    ViewBag.ReapplicationWindow = 30;

                    // Progress bar: how much of the 30 days has already elapsed
                    int daysElapsed = 30 - daysLeft;
                    ViewBag.ProgressPercent = (int)((daysElapsed / 30.0) * 100);
                }
            }

            return View();
        }

        // POST: Applicant/BookInterview
        [HttpPost]
        public ActionResult BookInterview(
         string FullName,
         string ContactNumber,
         string EmailAddress,
         IFormFile ResumeFile,
         IFormFile ValidIDFile,
         string PreferredDate,
         string PreferredTime,
         string AdditionalNotes)
        {
            if (!LoadLoggedInApplicant())
                return RedirectToAction("ApplicantLogin");

            int applicantId = HttpContext.Session.GetInt32("ApplicantId")!.Value;

            // ----- SERVER-SIDE COOLDOWN GUARD -----
            // Re-check the 30-day cooldown here too, since a determined user could
            // still POST directly to this action even with the button disabled client-side.
            var lastCompleted = _context.Appointments
                .Where(a => a.ApplicantId == applicantId && a.Status == "Completed" && a.DateCompleted != null)
                .OrderByDescending(a => a.DateCompleted)
                .FirstOrDefault();

            if (lastCompleted != null && lastCompleted.DateCompleted!.Value.AddDays(30) > DateTime.Today)
            {
                DateTime completedDate = lastCompleted.DateCompleted!.Value;
                DateTime eligibleDate = completedDate.AddDays(30);
                int daysLeft = (eligibleDate.Date - DateTime.Today).Days;
                int daysElapsed = 30 - daysLeft;

                ViewBag.InCooldown = true;
                ViewBag.DaysLeft = daysLeft;
                ViewBag.EligibleDate = eligibleDate.ToString("MMMM dd, yyyy");
                ViewBag.LastInterviewDate = lastCompleted.AppointmentDate.ToString("MMMM dd, yyyy");
                ViewBag.PositionApplied = lastCompleted.Purpose;
                ViewBag.ReapplicationWindow = 30;
                ViewBag.ProgressPercent = (int)((daysElapsed / 30.0) * 100);

                ViewBag.Error = "You are not yet eligible to book a new interview.";
                return View();
            }

            if (string.IsNullOrWhiteSpace(FullName) ||
                string.IsNullOrWhiteSpace(ContactNumber) ||
                string.IsNullOrWhiteSpace(EmailAddress) ||
                string.IsNullOrWhiteSpace(PreferredDate) ||
                string.IsNullOrWhiteSpace(PreferredTime))
            {
                ViewBag.Error = "Please fill in all required fields.";
                return View();
            }

            if (!DateTime.TryParse(PreferredDate, out DateTime parsedDate))
            {
                ViewBag.Error = "Invalid interview date.";
                return View();
            }

            if (!DateTime.TryParse(PreferredTime, out DateTime parsedTimeDateTime))
            {
                ViewBag.Error = "Invalid interview time.";
                return View();
            }
            TimeSpan parsedTime = parsedTimeDateTime.TimeOfDay;

            // ----- SAVE RESUME FILE -----
            string? savedResumeFileName = null;
            if (ResumeFile != null && ResumeFile.Length > 0)
            {
                savedResumeFileName = Guid.NewGuid().ToString() + Path.GetExtension(ResumeFile.FileName);
                string resumeFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Resumes");
                if (!Directory.Exists(resumeFolder))
                    Directory.CreateDirectory(resumeFolder);

                string resumePath = Path.Combine(resumeFolder, savedResumeFileName);
                using (var stream = new FileStream(resumePath, FileMode.Create))
                {
                    ResumeFile.CopyTo(stream);
                }
            }

            // ----- SAVE VALID ID FILE -----
            string? savedValidIDFileName = null;
            if (ValidIDFile != null && ValidIDFile.Length > 0)
            {
                savedValidIDFileName = Guid.NewGuid().ToString() + Path.GetExtension(ValidIDFile.FileName);
                string validIDFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "ValidIDs");
                if (!Directory.Exists(validIDFolder))
                    Directory.CreateDirectory(validIDFolder);

                string validIDPath = Path.Combine(validIDFolder, savedValidIDFileName);
                using (var stream = new FileStream(validIDPath, FileMode.Create))
                {
                    ValidIDFile.CopyTo(stream);
                }
            }

            // ----- SAVE TO DATABASE -----
            var newAppointment = new Appointment
            {
                ApplicantId = applicantId,
                Purpose = "Interview",
                AppointmentDate = parsedDate,
                AppointmentTime = parsedTime,
                Status = "Pending",
                DateRequested = DateTime.Now,
                ContactNumber = ContactNumber,
                Email = EmailAddress,
                ResumeFile = savedResumeFileName,
                ValidIDFile = savedValidIDFileName,
                AdditionalNotes = AdditionalNotes
            };

            _context.Appointments.Add(newAppointment);
            _context.SaveChanges();

            TempData["Success"] = "Interview booking submitted successfully!";
            return RedirectToAction("MyAppointment");
        }

        [HttpPost]
        public ActionResult CancelAppointment(int AppointmentId)
        {
            int? applicantId = HttpContext.Session.GetInt32("ApplicantId");
            if (applicantId == null)
                return RedirectToAction("ApplicantLogin");

            var appointment = _context.Appointments
                .FirstOrDefault(a => a.AppointmentId == AppointmentId && a.ApplicantId == applicantId);

            if (appointment == null)
            {
                ViewBag.Error = "Appointment not found.";
                return RedirectToAction("MyAppointment");
            }

            if (appointment.Status != "Pending")
            {
                ViewBag.Error = "This appointment can no longer be cancelled.";
                return RedirectToAction("MyAppointment");
            }

            _context.Appointments.Remove(appointment);  // <-- this line deletes it from SQL Server
            _context.SaveChanges();                      // <-- this line commits the delete

            TempData["CancelSuccess"] = true;
            return RedirectToAction("MyAppointment");
        }

        [HttpPost]
        public JsonResult EditAppointment([FromBody] EditAppointmentRequest request)
        {
            int? applicantId = HttpContext.Session.GetInt32("ApplicantId");
            if (applicantId == null)
            {
                return Json(new { success = false, message = "Session expired. Please log in again." });
            }

            // Only allow editing your OWN pending appointment
            var appointment = _context.Appointments
                .FirstOrDefault(a => a.AppointmentId == request.AppointmentId && a.ApplicantId == applicantId);

            if (appointment == null)
            {
                return Json(new { success = false, message = "Appointment not found." });
            }

            if (appointment.Status != "Pending")
            {
                return Json(new { success = false, message = "Only pending appointments can be edited." });
            }

            // ----- VALIDATE -----
            if (string.IsNullOrWhiteSpace(request.EmailAddress) ||
                string.IsNullOrWhiteSpace(request.PreferredDate) ||
                string.IsNullOrWhiteSpace(request.PreferredTime))
            {
                return Json(new { success = false, message = "Please fill out all fields." });
            }

            if (!DateTime.TryParse(request.PreferredDate, out DateTime parsedDate))
            {
                return Json(new { success = false, message = "Invalid date." });
            }

            if (!DateTime.TryParse(request.PreferredTime, out DateTime parsedTimeDateTime))
            {
                return Json(new { success = false, message = "Invalid time." });
            }
            TimeSpan parsedTime = parsedTimeDateTime.TimeOfDay;

            // ----- UPDATE -----
            appointment.Email = request.EmailAddress;
            appointment.AppointmentDate = parsedDate;
            appointment.AppointmentTime = parsedTime;

            _context.SaveChanges();

            return Json(new { success = true, message = "Appointment updated successfully." });
        }

        public ActionResult Settings()
        {
            if (!LoadLoggedInApplicant())
                return RedirectToAction("ApplicantLogin");

            return View();
        }
    }
}