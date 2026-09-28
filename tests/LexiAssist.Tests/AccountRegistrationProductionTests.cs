using LexiAssist.Web.Controllers;
using LexiAssist.Web.ViewModels;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace LexiAssist.Tests;

public sealed class AccountRegistrationProductionTests
{
    [Fact]
    public void Development_self_registration_remains_available()
    {
        var httpContext = new DefaultHttpContext();
        var controller = new AccountController(
            signInManager: null!,
            userManager: null!,
            auditService: null!,
            environment: new TestEnvironment(Environments.Development))
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, new TestTempDataProvider())
        };

        Assert.IsType<ViewResult>(controller.Register());
    }

    [Fact]
    public async Task Production_self_registration_redirects_before_creating_an_unconfirmed_account()
    {
        var httpContext = new DefaultHttpContext();
        var controller = new AccountController(
            signInManager: null!,
            userManager: null!,
            auditService: null!,
            environment: new TestEnvironment(Environments.Production))
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, new TestTempDataProvider())
        };

        var getResult = Assert.IsType<RedirectToActionResult>(controller.Register());
        Assert.Equal(nameof(AccountController.Login), getResult.ActionName);

        var postResult = Assert.IsType<RedirectToActionResult>(await controller.Register(new RegisterViewModel()));
        Assert.Equal(nameof(AccountController.Login), postResult.ActionName);
        Assert.Contains("contact your LexiAssist administrator", controller.TempData["StatusMessage"]?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TestEnvironment(string environmentName) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "LexiAssist.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = Path.GetTempPath();
        public string EnvironmentName { get; set; } = environmentName;
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
