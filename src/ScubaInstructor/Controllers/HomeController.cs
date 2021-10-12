using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using ScubaInstructor.Models;

namespace ScubaInstructor.Controllers
{
    public class HomeController : Controller
    {
        public ActionResult Index()
        {
            Debug.WriteLine("Index Invoked!");
            ViewBag.Title = "Home Page";

            return View(new MainModel());
        }
    }
}
