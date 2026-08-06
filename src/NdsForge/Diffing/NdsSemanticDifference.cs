namespace NdsForge;

/// <summary>Describes one stable manifest path whose semantic value differs between two images.</summary>
public sealed class NdsSemanticDifference
{
    /// <summary>Captures a normalized difference after the comparer has assigned logical identity and change kind.</summary>
    /// <param name="path">Stable dotted path such as <c>Header.GameCode</c> or <c>Files[/data/a.bin].Sha256</c>.</param>
    /// <param name="kind">Addition, removal, content change, relocation, renumbering, or move classification.</param>
    /// <param name="before">Invariant left-side representation, or <see langword="null"/> for an addition.</param>
    /// <param name="after">Invariant right-side representation, or <see langword="null"/> for a removal.</param>
    internal NdsSemanticDifference(string path, NdsDifferenceKind kind, string? before, string? after)
    {
        Path = path;
        Kind = kind;
        Before = before;
        After = after;
    }

    /// <summary>Identifies the exact stable manifest value or logical component that changed.</summary>
    public string Path { get; }
    /// <summary>Separates content edits from layout-only moves and numeric identity changes.</summary>
    public NdsDifferenceKind Kind { get; }
    /// <summary>Contains the invariant left value, or remains absent when the component was added.</summary>
    public string? Before { get; }
    /// <summary>Contains the invariant right value, or remains absent when the component was removed.</summary>
    public string? After { get; }
}
