using Microsoft.AspNetCore.Http;

namespace LexiAssist.Web.Services;

public sealed record ListeningAudioUploadResult(
    bool Success,
    string? FileName = null,
    string? ContentType = null,
    long SizeBytes = 0,
    int? DurationSeconds = null,
    string? ErrorMessage = null);

/// <summary>Validates and stores administrator authored listening stimuli outside wwwroot.</summary>
public sealed class ListeningAudioService(IWebHostEnvironment environment, ILogger<ListeningAudioService> logger)
{
    public const long MaxFileSizeBytes = 10 * 1024 * 1024;
    public const int MaxDurationSeconds = 120;
    private const string OwnedPrefix = "listening-";

    private string StorageDirectory => Path.Combine(environment.ContentRootPath, "App_Data", "ScreeningAudio");

    public bool IsOwnedFile(string? fileName) =>
        ScreeningAudioSource.TryGetContentType(fileName, out var safeName, out _) &&
        safeName.StartsWith(OwnedPrefix, StringComparison.OrdinalIgnoreCase);

    public async Task<ListeningAudioUploadResult> SaveAsync(IFormFile? file, string source, CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length <= 0)
        {
            return new(false, ErrorMessage: "Choose a non-empty audio file first.");
        }

        if (file.Length > MaxFileSizeBytes)
        {
            return new(false, ErrorMessage: "Audio files must be 10 MB or smaller.");
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var contentType = extension switch
        {
            ".wav" => "audio/wav",
            ".mp3" => "audio/mpeg",
            ".m4a" => "audio/mp4",
            ".ogg" => "audio/ogg",
            ".webm" => "audio/webm",
            ".aac" => "audio/aac",
            _ => null
        };
        if (contentType is null)
        {
            return new(false, ErrorMessage: "Use a WAV, MP3, M4A, OGG, WebM, or AAC audio file.");
        }

        Directory.CreateDirectory(StorageDirectory);
        var generatedName = $"{OwnedPrefix}{Guid.NewGuid():N}{extension}";
        var finalPath = Path.Combine(StorageDirectory, generatedName);
        var temporaryPath = Path.Combine(StorageDirectory, $".{generatedName}.upload");
        try
        {
            await using (var input = file.OpenReadStream())
            await using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
            {
                await input.CopyToAsync(output, cancellationToken);
            }

            int? duration;
            await using (var validationStream = new FileStream(temporaryPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true))
            {
                var header = new byte[16];
                var read = await validationStream.ReadAsync(header.AsMemory(0, header.Length), cancellationToken);
                if (!HasExpectedSignature(extension, header, read))
                {
                    return new(false, ErrorMessage: "The selected file is not a valid audio file.");
                }

                duration = extension == ".wav" ? TryReadWavDurationSeconds(temporaryPath) : null;
            }

            if (duration is > MaxDurationSeconds)
            {
                return new(false, ErrorMessage: "Audio must be no longer than two minutes.");
            }

            File.Move(temporaryPath, finalPath, overwrite: false);
            return new(true, generatedName, contentType, file.Length, duration);
        }
        catch (OperationCanceledException)
        {
            return new(false, ErrorMessage: "The audio upload was cancelled.");
        }
        catch (IOException exception)
        {
            logger.LogError(exception, "Could not store administrator listening audio upload.");
            return new(false, ErrorMessage: "The audio file could not be stored safely.");
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    public bool TryDeleteOwned(string? fileName)
    {
        if (!IsOwnedFile(fileName) || !ScreeningAudioSource.TryResolve(fileName, environment.ContentRootPath, out var fullPath, out _))
        {
            return false;
        }

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
        ".wav" => length >= 12 && header[0] == 'R' && header[1] == 'I' && header[2] == 'F' && header[3] == 'F' &&
                  header[8] == 'W' && header[9] == 'A' && header[10] == 'V' && header[11] == 'E',
        ".mp3" => length >= 3 && ((header[0] == 'I' && header[1] == 'D' && header[2] == '3') ||
                                  (header[0] == 0xFF && (header[1] & 0xE0) == 0xE0)),
        ".m4a" => length >= 8 && header[4] == 'f' && header[5] == 't' && header[6] == 'y' && header[7] == 'p',
        ".ogg" => length >= 4 && header[0] == 'O' && header[1] == 'g' && header[2] == 'g' && header[3] == 'S',
        ".webm" => length >= 4 && header[0] == 0x1A && header[1] == 0x45 && header[2] == 0xDF && header[3] == 0xA3,
        ".aac" => length >= 2 && header[0] == 0xFF && (header[1] & 0xF6) == 0xF0,
        _ => false
    };

    private static int? TryReadWavDurationSeconds(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            if (new string(reader.ReadChars(4)) != "RIFF") return null;
            _ = reader.ReadInt32();
            if (new string(reader.ReadChars(4)) != "WAVE") return null;
            ushort channels = 0, bitsPerSample = 0;
            uint sampleRate = 0, dataSize = 0;
            while (stream.Position + 8 <= stream.Length)
            {
                var chunk = new string(reader.ReadChars(4));
                var size = reader.ReadInt32();
                if (size < 0 || stream.Position + size > stream.Length) return null;
                if (chunk == "fmt ")
                {
                    _ = reader.ReadUInt16();
                    channels = reader.ReadUInt16();
                    sampleRate = reader.ReadUInt32();
                    _ = reader.ReadUInt32();
                    _ = reader.ReadUInt16();
                    bitsPerSample = reader.ReadUInt16();
                    stream.Position += Math.Max(0, size - 16);
                }
                else if (chunk == "data")
                {
                    dataSize = (uint)size;
                    break;
                }
                else
                {
                    stream.Position += size + (size % 2);
                }
            }

            if (sampleRate == 0 || channels == 0 || bitsPerSample == 0 || dataSize == 0) return null;
            var seconds = dataSize / (double)(sampleRate * channels * (bitsPerSample / 8d));
            return (int)Math.Ceiling(seconds);
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
    }
}
