using System.Security.Cryptography;

namespace NdsForge.CompatibilityTests.Corpus;

/// <summary>Replays parser, metadata, and validator observations against every exact SHA-bound private fixture.</summary>
[Collection(PrivateCorpusSerialGroup.Name)]
public sealed class PrivateCorpusIdentityTests
{
    /// <summary>Proves that content-addressed fixture resolution and all public identity fields agree with the original cataloging pass.</summary>
    [Theory]
    [MemberData(nameof(CorpusExpectations.Cases), MemberType = typeof(CorpusExpectations))]
    public async Task ParsesExpectedIdentityAndDiagnostics(CorpusExpectationIndexEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        CorpusExpectation expected = CorpusExpectations.Read(entry);
        string path = CorpusExpectations.Resolve(entry);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        Assert.Equal(expected.Rom.Length, new FileInfo(path).Length);
        await using (FileStream stream = File.OpenRead(path))
        {
            string hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(true));
            Assert.Equal(expected.Rom.Sha256, hash, ignoreCase: true);
        }

        using NdsImage image = await NdsImage.OpenAsync(path, cancellationToken: cancellationToken).ConfigureAwait(true);
        Assert.Equal(expected.Rom.Length, image.Length);
        Assert.Equal(expected.Rom.HeaderTitle, image.Header.Title);
        Assert.Equal(expected.Rom.GameCode, image.Header.GameCode);
        Assert.Equal(expected.Rom.MakerCode, image.Header.MakerCode);
        Assert.Equal(expected.Rom.Kind, image.Header.Kind);
        Assert.Equal(expected.Rom.Revision, image.Header.Version);

        IReadOnlyDictionary<string, string> actualTitles = image.Banner?.Titles
            .Where(static item => !string.IsNullOrWhiteSpace(item.Value))
            .ToDictionary(
            static item => item.Key.ToString(),
            static item => item.Value,
            StringComparer.Ordinal) ?? new Dictionary<string, string>();
        Assert.Equal(expected.Rom.BannerTitles.OrderBy(static item => item.Key), actualTitles.OrderBy(static item => item.Key));

        ExpectedDiagnostic[] actualDiagnostics = image.Validate().Diagnostics.Select(static diagnostic => new ExpectedDiagnostic(
            diagnostic.Code,
            diagnostic.Severity,
            diagnostic.Region?.Offset,
            diagnostic.Region?.Length)).ToArray();
        Assert.Equal(expected.ValidationDiagnostics, actualDiagnostics);
    }

    /// <summary>Guards the rule that every recorded operation has a corresponding NdsForge differential assertion.</summary>
    [Theory]
    [MemberData(nameof(CorpusExpectations.Cases), MemberType = typeof(CorpusExpectations))]
    public void RecordsCompleteNdstoolOperationContract(CorpusExpectationIndexEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        CorpusExpectation expected = CorpusExpectations.Read(entry);
        string[] operationNames = expected.Operations.Select(static operation => operation.Name).ToArray();
        Assert.Equal(
            [
                "extract-all", "create-binary", "repair-header-crc", "hook-arm7",
            ],
            operationNames);
        Assert.All(expected.Operations, static operation => Assert.Equal(0, operation.ExitCode));
        Assert.All(expected.Operations, static operation =>
        {
            Assert.Equal(64, operation.StandardOutputSha256.Length);
            Assert.Equal(64, operation.StandardErrorSha256.Length);
            Assert.Equal(operation.Artifacts.Count, operation.Artifacts.Select(static artifact => artifact.Path).Distinct(StringComparer.Ordinal).Count());
        });
    }
}
