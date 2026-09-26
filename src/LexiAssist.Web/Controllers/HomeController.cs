using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using LexiAssist.Web.Models;

namespace LexiAssist.Web.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> logger;

    public HomeController(ILogger<HomeController> logger)
    {
        this.logger = logger;
    }

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
        var requestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        var exceptionFeature = HttpContext.Features.Get<IExceptionHandlerPathFeature>();

        // Log only safe structured metadata. Exception messages and request bodies
        // are deliberately excluded because they may contain user supplied secrets.
        if (exceptionFeature?.Error is { } exception &&
            !HttpContext.Items.ContainsKey(nameof(Error)))
        {
            HttpContext.Items[nameof(Error)] = true;
            logger.LogError(
                "Unhandled request {RequestId} ({ExceptionType}) at {Path}",
                requestId,
                exception.GetType().Name,
                exceptionFeature.Path);
        }

        Response.StatusCode = StatusCodes.Status500InternalServerError;
        return View(new ErrorViewModel { RequestId = requestId });
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult StatusCodePage(int statusCode)
    {
        var requestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        // Preserve the original client/server status while selecting a safe view.
        var normalizedStatusCode = statusCode is >= 400 and <= 599
            ? statusCode
            : StatusCodes.Status500InternalServerError;

        Response.StatusCode = normalizedStatusCode;

        if (normalizedStatusCode == StatusCodes.Status404NotFound)
        {
            return View("NotFound", new ErrorViewModel { RequestId = requestId, StatusCode = normalizedStatusCode });
        }

        if (normalizedStatusCode == StatusCodes.Status403Forbidden)
        {
            return View("~/Views/Account/AccessDenied.cshtml", new ErrorViewModel
            {
                RequestId = requestId,
                StatusCode = normalizedStatusCode
            });
        }

        return View("Error", new ErrorViewModel { RequestId = requestId, StatusCode = normalizedStatusCode });
    }
}
