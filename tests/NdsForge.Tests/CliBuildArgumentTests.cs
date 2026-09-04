using NdsForge.Cli;

namespace NdsForge.Tests;

public sealed class CliBuildArgumentTests
{
    [Theory]
    [InlineData("")]
    [InlineData("--unknown")]
    [InlineData("--overwrite --overwrite")]
    [InlineData("--pad --pad")]
    [InlineData("--capacity")]
    [InlineData("--capacity 0")]
    [InlineData("--capacity -131072")]
    [InlineData("--capacity 65536")]
    [InlineData("--capacity 131073")]
    [InlineData("--capacity 0x100000001")]
    [InlineData("--capacity 0x8000000000000000")]
    [InlineData("--capacity FF")]
    [InlineData("--capacity 131072 --capacity 262144")]
    [InlineData("--padding-byte")]
    [InlineData("--padding-byte 0xFF")]
    [InlineData("--padding-byte F")]
    [InlineData("--padding-byte FG")]
    [InlineData("--ds-integrity")]
    [InlineData("--ds-integrity homebrew")]
    [InlineData("--dsi-integrity")]
    [InlineData("--dsi-integrity preserve")]
    [InlineData("--ds-integrity clear --dsi-integrity clear")]
    [InlineData("--no-verify")]
    public void InvalidArgumentsDoNotReadAnyWorkspace(string suffix)
    {
        ArgumentNullException.ThrowIfNull(suffix);
        string[] args = suffix.Length == 0 ? ["build", "workspace"] : ["build", "workspace", "out.nds", .. suffix.Split(' ')];
        Assert.Null(CliBuildArguments.Parse(args));
    }

    [Theory]
    [InlineData("131072", 131072L)]
    [InlineData("0x80000", 524288L)]
    [InlineData("0X100000000", 4294967296L)]
    public void ExplicitSizingRetainsVerificationAndRequestedByte(string capacity, long expected)
    {
        CliBuildArguments arguments = Assert.IsType<CliBuildArguments>(CliBuildArguments.Parse(
            ["build", "workspace", "out.nds", "--capacity", capacity, "--pad", "--padding-byte", "a5", "--overwrite"]));
        Assert.Equal(expected, arguments.BuildOptions.RequestedDeviceCapacityBytes);
        Assert.Equal(0xA5, arguments.BuildOptions.PaddingByte);
        Assert.True(arguments.BuildOptions.PadToDeviceCapacity);
        Assert.True(arguments.BuildOptions.OverwriteDestination);
        Assert.True(arguments.BuildOptions.VerifyOutput);
        Assert.Equal(NdsImageBuildProfile.Deterministic, arguments.BuildOptions.Profile);
    }

    [Fact]
    public void DefaultsDoNotInferAuthenticationOrCapacity()
    {
        CliBuildArguments arguments = Assert.IsType<CliBuildArguments>(CliBuildArguments.Parse(["build", "workspace", "out.nds"]));
        Assert.Equal(NdsImageBuildOptions.Default, arguments.BuildOptions);
        Assert.Null(arguments.DsIntegrity);
        Assert.Null(arguments.DsiIntegrity);
    }
}
