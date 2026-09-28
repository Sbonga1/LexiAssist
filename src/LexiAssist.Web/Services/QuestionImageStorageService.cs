using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace LexiAssist.Web.Services;

public sealed class QuestionImagesOptions
{
    public const string SectionName = "QuestionImages";

    public string? StorageDirectory { get; set; }
}

public sealed record QuestionImageUploadResult(
    bool Success,
    string? FileName = null,
    string? ContentType = null,
    long SizeBytes = 0,
    string? ErrorMessage = null);

public static class QuestionImageSource
{
    private const string OwnedPrefix = "question-image-";
    private const long MaximumStoredFileSizeBytes = 10L * 1024 * 1024;
    private static readonly IReadOnlyDictionary<string, string> ContentTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".gif"] = "image/gif",
        [".jpeg"] = "image/jpeg",
        [".jpg"] = "image/jpeg",
        [".png"] = "image/png",
        [".webp"] = "image/webp"
    };

    public static bool TryGetContentType(string? configuredFileName, out string fileName, out string contentType)
    {
        fileName = string.Empty;
        contentType = string.Empty;
        if (string.IsNullOrWhiteSpace(configuredFileName)) return false;

        var normalized = configuredFileName.Trim();
        if (normalized.Length > 100 || normalized is "." or ".." || normalized.Contains('/') || normalized.Contains('\\') ||
            normalized.Contains(':') || normalized.Any(char.IsControl) ||
            !string.Equals(normalized, Path.GetFileName(normalized), StringComparison.Ordinal) ||
            !normalized.StartsWith(OwnedPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var extension = Path.GetExtension(normalized);
        if (!ContentTypes.TryGetValue(extension, out contentType!)) return false;
        fileName = normalized;
        return true;
    }

    public static bool TryGetStoredFileName(string? imagePath, out string fileName, out string contentType)
    {
        fileName = string.Empty;
        contentType = string.Empty;
        if (string.IsNullOrWhiteSpace(imagePath)) return false;
        var prefix = "/question-images/";
        var value = imagePath.Trim();
        if (!value.StartsWith(prefix, StringComparison.Ordinal) || value.Contains('?') || value.Contains('#'))
        {
            return false;
        }

        return TryGetContentType(value[prefix.Length..], out fileName, out contentType);
    }

    public static bool TryResolve(
        string? configuredFileName,
        string contentRootPath,
        out string fullPath,
        out string contentType,
        string? configuredDirectory = null)
    {
        fullPath = string.Empty;
        contentType = string.Empty;
        if (!TryGetContentType(configuredFileName, out var fileName, out contentType)) return false;

        try
        {
            var mediaRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(configuredDirectory)
                ? Path.Combine(contentRootPath, "App_Data", "QuestionImages")
                : Path.IsPathRooted(configuredDirectory)
                    ? configuredDirectory
                    : Path.Combine(contentRootPath, configuredDirectory));
            var candidatePath = Path.GetFullPath(Path.Combine(mediaRoot, fileName));
            if (!IsWithinDirectory(mediaRoot, candidatePath)) return false;

            var file = new FileInfo(candidatePath);
            if (!file.Exists || file.Length is <= 0 or > MaximumStoredFileSizeBytes) return false;
            var resolvedPath = file.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? file.FullName;
            if (!IsWithinDirectory(mediaRoot, Path.GetFullPath(resolvedPath))) return false;

            fullPath = candidatePath;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsWithinDirectory(string directory, string candidate)
    {
        var directoryWithSeparator = Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return candidate.StartsWith(directoryWithSeparator, comparison);
    }
}

/// <summary>Stores validated question images outside the web root for persistent, private delivery.</summary>
public sealed class QuestionImageStorageService(
    IWebHostEnvironment environment,
    ILogger<QuestionImageStorageService> logger,
    IOptions<QuestionImagesOptions> options)
{
    public const long MaxFileSizeBytes = 5 * 1024 * 1024;

    private readonly QuestionImagesOptions _options = options.Value;
    private string StorageDirectory
    {
        get
        {
            var directory = Path.GetFullPath(string.IsNullOrWhiteSpace(_options.StorageDirectory)
                ? Path.Combine(environment.ContentRootPath, "App_Data", "QuestionImages")
                : Path.IsPathRooted(_options.StorageDirectory)
                    ? _options.StorageDirectory
                    : Path.Combine(environment.ContentRootPath, _options.StorageDirectory));
            var webRoot = Path.GetFullPath(environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot"));
            var webRootWithSeparator = Path.TrimEndingDirectorySeparator(webRoot) + Path.DirectorySeparatorChar;
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (string.Equals(directory, webRoot, comparison) || directory.StartsWith(webRootWithSeparator, comparison))
                throw new InvalidOperationException("Question image storage must be outside the public web root.");
            return directory;
        }
    }

    public async Task<QuestionImageUploadResult> SaveAsync(IFormFile? file, CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length <= 0)
            return new(false, ErrorMessage: "Choose a non-empty image file first.");
        if (file.Length > MaxFileSizeBytes)
            return new(false, ErrorMessage: "Question images must be 5 MB or smaller.");

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var contentType = extension switch
        {
            ".gif" => "image/gif",
            ".jpeg" or ".jpg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => null
        };
        if (contentType is null)
            return new(false, ErrorMessage: "Use a PNG, JPEG, WebP, or GIF image.");

        Directory.CreateDirectory(StorageDirectory);
        var generatedName = $"question-image-{Guid.NewGuid():N}{extension}";
        var finalPath = Path.Combine(StorageDirectory, generatedName);
        var temporaryPath = Path.Combine(StorageDirectory, $".{generatedName}.upload");
        try
        {
            await using (var input = file.OpenReadStream())
            await using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
            {
                var buffer = new byte[64 * 1024];
                long totalBytes = 0;
                int bytesRead;
                while ((bytesRead = await input.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
                {
                    totalBytes += bytesRead;
                    if (totalBytes > MaxFileSizeBytes)
                        return new(false, ErrorMessage: "Question images must be 5 MB or smaller.");
                    await output.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                }
                if (totalBytes == 0)
                    return new(false, ErrorMessage: "Choose a non-empty image file first.");
            }

            await using (var validationStream = new FileStream(temporaryPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true))
            {
                var header = new byte[12];
                var read = await validationStream.ReadAsync(header.AsMemory(), cancellationToken);
                if (!HasExpectedSignature(extension, header, read))
                    return new(false, ErrorMessage: "The selected file does not match a supported image format.");
            }

            File.Move(temporaryPath, finalPath, overwrite: false);
            return new(true, generatedName, contentType, new FileInfo(finalPath).Length);
        }
        catch (OperationCanceledException)
        {
            return new(false, ErrorMessage: "The image upload was cancelled.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogError(exception, "Could not store administrator question image upload.");
            return new(false, ErrorMessage: "The image file could not be stored safely.");
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    public bool IsOwnedFile(string? fileName) => QuestionImageSource.TryGetContentType(fileName, out _, out _);

    public bool TryDeleteOwned(string? fileName)
    {
        if (!IsOwnedFile(fileName) || !QuestionImageSource.TryResolve(fileName, environment.ContentRootPath, out var fullPath, out _, _options.StorageDirectory))
            return false;
        try
        {
            File.Delete(fullPath);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static bool HasExpectedSignature(string extension, byte[] header, int length) => extension switch
    {
        ".gif" => length >= 6 && (header.AsSpan(0, 6).SequenceEqual("GIF87a"u8) || header.AsSpan(0, 6).SequenceEqual("GIF89a"u8)),
        ".jpeg" or ".jpg" => length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
        ".png" => length >= 8 && header.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
        ".webp" => length >= 12 && header[0] == 'R' && header[1] == 'I' && header[2] == 'F' && header[3] == 'F' &&
                   header[8] == 'W' && header[9] == 'E' && header[10] == 'B' && header[11] == 'P',
        _ => false
    };

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
    }
}
