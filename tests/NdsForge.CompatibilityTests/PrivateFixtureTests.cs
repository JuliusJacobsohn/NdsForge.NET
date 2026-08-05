namespace NdsForge.CompatibilityTests;

public sealed class PrivateFixtureTests
{
    [Fact]
    public void PrivateFixtureIsAvailableOnlyByOptIn()
    {
        string? path = Environment.GetEnvironmentVariable("NDSFORGE_TEST_ROM");
        if (string.IsNullOrWhiteSpace(path))
        {
            Assert.Skip("Set NDSFORGE_TEST_ROM to a legally obtained local image to run compatibility tests.");
        }

        Assert.True(File.Exists(path), $"Private fixture does not exist: {path}");
    }
}
