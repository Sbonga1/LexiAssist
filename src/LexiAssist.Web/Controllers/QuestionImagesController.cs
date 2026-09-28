using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Identity;
using LexiAssist.Web.Models.Screening;
using LexiAssist.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Hosting;

namespace LexiAssist.Web.Controllers;

[Authorize(Roles = ApplicationRoles.Administrator + "," + ApplicationRoles.Learner)]
[Route("question-images")]
public sealed class QuestionImagesController(
    ApplicationDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    IWebHostEnvironment environment,
    IOptions<QuestionImagesOptions> imageOptions) : Controller
{
    [HttpGet("{fileName}")]
    public async Task<IActionResult> Get(string fileName, CancellationToken cancellationToken)
    {
        if (!QuestionImageSource.TryGetContentType(fileName, out var safeFileName, out var contentType))
            return NotFound();

        var imagePath = $"/question-images/{safeFileName}";
        if (!await dbContext.ScreeningQuestions.AsNoTracking().AnyAsync(question => question.ImagePath == imagePath, cancellationToken))
            return NotFound();
        if (!User.IsInRole(ApplicationRoles.Administrator) && !await LearnerCanViewAsync(imagePath, cancellationToken))
            return NotFound();

        if (!QuestionImageSource.TryResolve(safeFileName, environment.ContentRootPath,
                out var fullPath, out contentType, imageOptions.Value.StorageDirectory))
            return NotFound();

        Response.Headers["Content-Disposition"] = "inline";
        Response.Headers["Cache-Control"] = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete), contentType);
    }

    private async Task<bool> LearnerCanViewAsync(string imagePath, CancellationToken cancellationToken)
    {
        var userId = userManager.GetUserId(User);
        if (userId is null) return false;

        var activeAssessmentUsesImage = await dbContext.ScreeningQuestions.AsNoTracking()
            .AnyAsync(question => question.ImagePath == imagePath && question.IsActive && question.ScreeningTest.IsActive, cancellationToken);
        if (activeAssessmentUsesImage) return true;

        return await dbContext.ScreeningQuestions.AsNoTracking()
            .AnyAsync(question => question.ImagePath == imagePath && question.IsActive &&
                dbContext.ScreeningTestAttempts.Any(testAttempt => testAttempt.ScreeningTestId == question.ScreeningTestId &&
                    testAttempt.ScreeningAttempt.UserId == userId &&
                    testAttempt.ScreeningAttempt.Status == ScreeningAttemptStatus.InProgress), cancellationToken);
    }
}
