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
}
