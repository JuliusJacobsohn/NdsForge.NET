namespace NdsForge;

/// <summary>Reports physical changes without treating the common used-size field as the end of meaningful data.</summary>
public sealed class NdsImageResizeResult
{
    /// <summary>Captures immutable lengths and any explicit trailing-data-discard warning.</summary>
    internal NdsImageResizeResult(long inputLength, long outputLength, IReadOnlyList<NdsDiagnostic> diagnostics)
    {
        InputLength = inputLength;
        OutputLength = outputLength;
        Diagnostics = diagnostics;
    }

    /// <summary>Gets the source's complete physical byte count, including any unclassified trailing material.</summary>
    public long InputLength { get; }

    /// <summary>Gets the completed output's physical byte count.</summary>
    public long OutputLength { get; }

    /// <summary>Gets the removed trailing interval, or null when no bytes were removed.</summary>
    public NdsRegion? RemovedData => OutputLength < InputLength ? new(OutputLength, InputLength - OutputLength) : null;

    /// <summary>Gets the newly added padding interval, or null when no bytes were added.</summary>
    public NdsRegion? AddedData => OutputLength > InputLength ? new(InputLength, OutputLength - InputLength) : null;

    /// <summary>Reports conservative extent warnings and explicitly discarded unclassified data.</summary>
    public IReadOnlyList<NdsDiagnostic> Diagnostics { get; }
}
