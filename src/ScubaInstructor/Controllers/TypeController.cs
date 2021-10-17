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
    public class TypeController : Controller
    {
        public ActionResult Index()
        {
            TypeDump dump = null;
            string name = Request.QueryString["name"];
            if (name == null)
            {
                return Json(new { status = "error", message = "Missing parameter 'name'" }, JsonRequestBehavior.AllowGet);
            }
            if (name != null && !TypesCache.Instance.TryGetCached(name, out dump))
            {
                ViewBag.Title = "TypeFullName Dump";
                HttpClient c = new HttpClient();
                string url = "http://127.0.0.1:9977/type" +
                             (Request.QueryString.AllKeys.Any() ? $"?{Request.QueryString}" : String.Empty);
                HttpResponseMessage res = c.GetAsync(url).Result;
                var body = res.Content.ReadAsStringAsync().Result;
                try
                {
                    dump = System.Text.Json.JsonSerializer.Deserialize<TypeDump>(body);
                }
                catch (Exception e)
                {   
                    if (body.Contains("error"))
                    {
                        return new JsonResult() {Data = body, JsonRequestBehavior = JsonRequestBehavior.AllowGet};
                    }
                    return Json(new { status = "error", message = $"Failed to deserialize response. Exception: {e}, Raw response: {body}"}, JsonRequestBehavior.AllowGet);
                }

                TypesCache.Instance.Update(name, dump);

            }

            return View(dump);
        }
    }
}
