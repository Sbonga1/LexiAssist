namespace LexiAssist.Web.Services;

public static class ScreeningRecordingSource
{
    private const long MaximumFileSizeBytes = 100L * 1024 * 1024;

    private static readonly IReadOnlyDictionary<string, string> ContentTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".aac"] = "audio/aac",
        [".m4a"] = "audio/mp4",
        [".mp3"] = "audio/mpeg",
        [".ogg"] = "audio/ogg",
        [".wav"] = "audio/wav",
        [".webm"] = "audio/webm"
    };

    public static bool TryResolve(
        string? storedFileName,
        string contentRootPath,
        out string fullPath,
        out string contentType)
    {
        fullPath = string.Empty;
        contentType = string.Empty;
        if (string.IsNullOrWhiteSpace(storedFileName))
        {
            return false;
        }

        var fileName = storedFileName.Trim();
        if (fileName.Length > 255 || fileName is "." or ".." ||
            fileName.Contains('/') || fileName.Contains('\\') || fileName.Contains(':') ||
            fileName.Any(char.IsControl) || !string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal))
        {
            return false;
        }

        if (!ContentTypes.TryGetValue(Path.GetExtension(fileName), out contentType!))
        {
            return false;
        }

        try
        {
            var mediaRoot = Path.GetFullPath(Path.Combine(contentRootPath, "App_Data", "ScreeningRecordings"));
            var candidatePath = Path.GetFullPath(Path.Combine(mediaRoot, fileName));
            if (!IsWithinDirectory(mediaRoot, candidatePath))
            {
                return false;
            }

            var file = new FileInfo(candidatePath);
            if (!file.Exists || file.Length is <= 0 or > MaximumFileSizeBytes)
            {
                return false;
            }

            var resolvedPath = file.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? file.FullName;
            if (!IsWithinDirectory(mediaRoot, Path.GetFullPath(resolvedPath)))
            {
                return false;
            }

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
