using System.Collections.ObjectModel;

namespace NdsForge;

/// <summary>Collects deterministic semantic, identity, and layout differences between two manifest snapshots.</summary>
public sealed class NdsImageDiff
{
    /// <summary>Freezes comparer output in stable path order so repeated CI reports are byte-for-byte reproducible.</summary>
    /// <param name="leftImageSha256">Physical identity of the left snapshot.</param>
    /// <param name="rightImageSha256">Physical identity of the right snapshot.</param>
    /// <param name="differences">Complete unsorted findings accumulated by the comparer.</param>
    internal NdsImageDiff(string leftImageSha256, string rightImageSha256, IEnumerable<NdsSemanticDifference> differences)
    {
        LeftImageSha256 = leftImageSha256;
        RightImageSha256 = rightImageSha256;
        Differences = new ReadOnlyCollection<NdsSemanticDifference>(differences
            .OrderBy(static value => value.Path, StringComparer.Ordinal)
            .ThenBy(static value => value.Kind)
            .ToArray());
    }

    /// <summary>Records the full physical SHA-256 identity of the comparison baseline.</summary>
    public string LeftImageSha256 { get; }
    /// <summary>Records the full physical SHA-256 identity of the comparison target.</summary>
    public string RightImageSha256 { get; }
    /// <summary>Contains every detected value change in deterministic path order.</summary>
    public IReadOnlyList<NdsSemanticDifference> Differences { get; }
    /// <summary>Reports whether header, content, identities, and physical layout all compare equal.</summary>
    public bool AreEquivalent => Differences.Count == 0;
}
