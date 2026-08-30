namespace NdsForge;

/// <summary>Controls physical resizing while preserving retained source bytes and all header declarations exactly.</summary>
public sealed record NdsImageResizeOptions
{
    /// <summary>Copies the complete input, verifies output, and refuses implicit path replacement.</summary>
    public static NdsImageResizeOptions Default { get; } = new();

    /// <summary>Selects preservation, bounded trimming, capacity expansion, or an explicit physical length.</summary>
    public NdsImageResizeMode Mode { get; init; }

    /// <summary>Supplies the exclusive output length for <see cref="NdsImageResizeMode.ExactLength"/> only.</summary>
    public long? OutputLengthBytes { get; init; }

    /// <summary>Supplies expansion bytes and the exact byte required when verifying removed padding; defaults to 0xFF.</summary>
    public byte PaddingByte { get; init; } = 0xFF;

    /// <summary>Requires confirmed padding by default; unclassified trailing bytes are never implicitly discarded.</summary>
    public NdsTrailingDataPolicy TrailingDataPolicy { get; init; }

    /// <summary>Validates the source and output and compares every retained byte and every added padding byte.</summary>
    public bool VerifyOutput { get; init; } = true;

    /// <summary>Allows successful path output to atomically replace an existing regular file.</summary>
    public bool OverwriteDestination { get; init; }

    /// <summary>Rejects undefined modes and contradictory length requests before any output mutation.</summary>
    internal void Validate()
    {
        if (!Enum.IsDefined(Mode) || !Enum.IsDefined(TrailingDataPolicy))
        {
            throw new ArgumentException("Resize mode and trailing-data policy must be defined values.");
        }
        if ((Mode == NdsImageResizeMode.ExactLength) != OutputLengthBytes.HasValue)
        {
            throw new ArgumentException("An explicit output length is required only for exact-length resizing.");
        }
        if (OutputLengthBytes is <= 0 or > 0x100000000L)
        {
            throw new ArgumentException("Explicit output length must be positive and no greater than 4 GiB.");
        }
    }
}
