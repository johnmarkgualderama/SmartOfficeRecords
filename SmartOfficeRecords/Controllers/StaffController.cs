using Microsoft.AspNetCore.Mvc;

namespace SmartOfficeRecords.Controllers
{
    public class StaffController : Controller
    {
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
            return View();
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
    }
}
