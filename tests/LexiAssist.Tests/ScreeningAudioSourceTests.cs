using LexiAssist.Web.Services;

namespace LexiAssist.Tests;

public sealed class ScreeningAudioSourceTests
{
    [Fact]
    public void Development_listening_fixture_is_a_non_empty_wav_and_rejects_path_traversal()
    {
        var webProjectRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "LexiAssist.Web"));
        const string fixtureName = "development-listening-blue-bird.wav";

        Assert.True(ScreeningAudioSource.TryResolve(
            fixtureName,
            webProjectRoot,
            out var fixturePath,
            out var contentType));
        Assert.Equal("audio/wav", contentType);

        var bytes = File.ReadAllBytes(fixturePath);
        Assert.True(bytes.Length > 44);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(bytes, 8, 4));
        Assert.False(ScreeningAudioSource.TryResolve(
            "../development-listening-blue-bird.wav",
            webProjectRoot,
            out _,
            out _));
    }

    [Fact]
    public void Configured_persistent_audio_directory_is_used_for_resolution()
    {
        var tempRoot = Path.GetFullPath(Path.GetTempPath());
        var storageDirectory = Path.Combine(tempRoot, $"lexiassist-screening-audio-{Guid.NewGuid():N}");
        Directory.CreateDirectory(storageDirectory);
        var fileName = "listening-test.wav";
        var expectedPath = Path.Combine(storageDirectory, fileName);
        File.WriteAllBytes(expectedPath, [1, 2, 3, 4]);

        try
        {
            Assert.True(ScreeningAudioSource.TryResolve(
                fileName,
                Path.Combine(tempRoot, "application-content"),
                out var actualPath,
                out var contentType,
                storageDirectory));
            Assert.Equal(expectedPath, actualPath);
            Assert.Equal("audio/wav", contentType);
        }
        finally
        {
            File.Delete(expectedPath);
            Directory.Delete(storageDirectory);
        }
    }
}
