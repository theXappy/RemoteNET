using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Web;
using System.Web.Mvc;
using Microsoft.Ajax.Utilities;
using ScubaDiver;
using ScubaDiver.API;
using ScubaInstructor.Helpers;
using ScubaInstructor.Models;

namespace ScubaInstructor.Controllers
{
    public class TypesController : Controller
    {
        public ActionResult Index()
        {
            TypesDump dump = null;
            string name = Request.QueryString["assembly"];
            if (name == null)
            {
                return Json(new { status = "error", message = "Missing parameter 'assembly'" }, JsonRequestBehavior.AllowGet);
            }
            {
                ViewBag.Title = "Types Dump";
                HttpClient c = new HttpClient();
                string url = "http://127.0.0.1:9977/types" +
                             (Request.QueryString.AllKeys.Any() ? $"?{Request.QueryString}" : String.Empty);
                HttpResponseMessage res = c.GetAsync(url).Result;
                var body = res.Content.ReadAsStringAsync().Result;
                try
                {
                    dump = System.Text.Json.JsonSerializer.Deserialize<TypesDump>(body);
                }
                catch (Exception e)
                {   
                    if (body.Contains("error"))
                    {
                        return new JsonResult() {Data = body, JsonRequestBehavior = JsonRequestBehavior.AllowGet};
                    }
                    return Json(new { status = "error", message = $"Failed to deserialize response. Exception: {e}, Raw response: {body}"}, JsonRequestBehavior.AllowGet);
                }
            }

            return View(dump);
        }
    }
}
