namespace NdsForge.Tests;

public sealed class NdsOverlayTests
{
    [Fact]
    public void ResolvesPayloadByFileIdInsteadOfOverlayId()
    {
        using NdsImage image = NdsImage.Load(SyntheticImage.CreateWithOverlay());

        NdsOverlay overlay = Assert.Single(image.Arm9Overlays);

        Assert.Equal(7u, overlay.Id);
        Assert.Equal(0u, overlay.FileId);
        Assert.Equal(new NdsRegion(0x228, 5), overlay.Data);
        Assert.Equal("/hello.bin", overlay.File?.FullPath);
        Assert.Equal(5u, overlay.CompressedSize);
        Assert.Equal(1, overlay.Flags);
    }

    [Fact]
    public void ValidationReportsMissingOverlayFileId()
    {
        byte[] data = SyntheticImage.CreateWithOverlay();
        data[0x248] = 42;
        using NdsImage image = NdsImage.Load(data);

        NdsDiagnostic diagnostic = Assert.Single(
            image.Validate().Diagnostics,
            static value => value.Code == "NDS1201");

        Assert.Contains("file ID 42", diagnostic.Message, StringComparison.Ordinal);
    }
}
