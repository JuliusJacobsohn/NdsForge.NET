namespace NdsForge.Tests;

public sealed class NdsForgeVersionTests
{
    [Fact]
    public void TargetFrameworkIsNet10()
    {
        Assert.Equal("net10.0", NdsForgeVersion.TargetFramework);
    }
}
