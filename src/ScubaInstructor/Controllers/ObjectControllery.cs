using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web;
using System.Web.Http;
using System.Web.Mvc;
using ScubaDiver;
using ScubaDiver.API;
using ScubaInstructor.Models;

namespace ScubaInstructor.Controllers
{
    public class ObjectController : Controller
    {
        public ActionResult Index()
        {
            string addr = Request.QueryString["address"];
            if (addr == null)
            {
                return Json(new { status = "error", message = "Missing parameter 'address'" }, JsonRequestBehavior.AllowGet);
            }
            Debug.WriteLine("Index Invoked! " + addr +" ~ ");
            ViewBag.Title = $"Object Dump ({addr})";
            HttpClient c = new HttpClient();
            string url = "http://127.0.0.1:9977/object" +
                         (Request.QueryString.AllKeys.Any() ? $"?{Request.QueryString}" : String.Empty);
            HttpResponseMessage res = c.GetAsync(url).Result;
            var body = res.Content.ReadAsStringAsync().Result;
            ObjectDump objDump;
            try
            {
                objDump = System.Text.Json.JsonSerializer.Deserialize<ObjectDump>(body);
            }
            catch (Exception e)
            {
                return Json(new { status = "error", message = e.Message }, JsonRequestBehavior.AllowGet);
            }




            return View(objDump);
        }
    }
}
