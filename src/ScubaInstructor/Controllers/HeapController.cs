using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Web;
using System.Web.Mvc;
using ScubaDiver;
using ScubaInstructor.Models;

namespace ScubaInstructor.Controllers
{
    public class HeapController : Controller
    {
        public ActionResult Index()
        {
            string type_filter = Request.QueryString["type_filter"];
            Debug.WriteLine("Index Invoked! " + type_filter +" ~ ");
            ViewBag.Title = "Home Page";
            HttpClient c = new HttpClient();
            string url = "http://127.0.0.1:9977/heap" +
                         (Request.QueryString.AllKeys.Any() ? $"?{Request.QueryString}" : String.Empty);
            HttpResponseMessage res = c.GetAsync(url).Result;
            var body = res.Content.ReadAsStringAsync().Result;
            HeapDump heapDump = System.Text.Json.JsonSerializer.Deserialize<HeapDump>(body);

            return View(heapDump);
        }
    }
}
