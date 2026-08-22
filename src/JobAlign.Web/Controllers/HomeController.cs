using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using JobAlign.Web.Models;

namespace JobAlign.Web.Controllers;

/// <summary>
/// Landing, privacy and error pages. Anonymous because these are what an unauthenticated
/// visitor sees; the rest of the application is closed by the fallback policy (NFR-04).
/// </summary>
[AllowAnonymous]
public class HomeController : Controller
{
    public IActionResult Index()
    {
        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
