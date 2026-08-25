namespace NdsForge;

/// <summary>Supplies the decoded ARM9 table location, storage form, and key needed for atomic overlay HMAC repair.</summary>
public sealed class NdsOverlayAuthenticationBuildOptions
{
    /// <summary>Owns caller key bytes independently from later buffer mutation.</summary>
    private readonly byte[]? _hmacKey;
    /// <summary>Retains an import-specific reason when source records could not establish repair material.</summary>
    private readonly string? _unrepairableReason;

    /// <summary>Captures fully validated public or imported settings.</summary>
    private NdsOverlayAuthenticationBuildOptions(
        uint tableRelativeOffset,
        NdsProgramStorageEncoding programStorage,
        int uncompressedPrefixLength,
        byte[]? hmacKey,
        string? unrepairableReason)
    {
        TableRelativeOffset = tableRelativeOffset;
        ProgramStorage = programStorage;
        UncompressedPrefixLength = uncompressedPrefixLength;
        _hmacKey = hmacKey;
        _unrepairableReason = unrepairableReason;
    }

    /// <summary>
    /// Creates a repair policy for caller-authored or imported ARM9 bytes whose table storage has already been reserved.
    /// NdsForge writes no key into the program; it uses only this copied key to update the existing records.
    /// </summary>
    /// <param name="tableRelativeOffset">Nonzero decoded ARM9 offset of the first 20-byte record.</param>
    /// <param name="hmacKey">Non-empty HMAC-SHA1 key authorized by the caller.</param>
    /// <param name="programStorage">Whether supplied ARM9 bytes are plain or BLZ encoded.</param>
    /// <param name="uncompressedPrefixLength">Minimum verbatim prefix retained when re-encoding BLZ.</param>
    /// <returns>An immutable policy that owns its key copy.</returns>
    public static NdsOverlayAuthenticationBuildOptions CreateHmacSha1(
        uint tableRelativeOffset,
        ReadOnlySpan<byte> hmacKey,
        NdsProgramStorageEncoding programStorage = NdsProgramStorageEncoding.Plain,
        int uncompressedPrefixLength = 0)
    {
        if (tableRelativeOffset == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tableRelativeOffset), "An authentication table cannot begin at decoded offset zero.");
        }

        if (hmacKey.IsEmpty)
        {
            throw new ArgumentException("An overlay authentication HMAC key cannot be empty.", nameof(hmacKey));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(uncompressedPrefixLength);
        if (programStorage == NdsProgramStorageEncoding.Plain && uncompressedPrefixLength != 0)
        {
            throw new ArgumentException("Plain ARM9 storage does not use a BLZ prefix length.", nameof(uncompressedPrefixLength));
        }

        return new(
            tableRelativeOffset,
            programStorage,
            uncompressedPrefixLength,
            hmacKey.ToArray(),
            unrepairableReason: null);
    }

    /// <summary>Locates the first 20-byte digest in the runtime ARM9 representation rather than the compressed file.</summary>
    public uint TableRelativeOffset { get; }

    /// <summary>Determines whether changed table bytes are written directly or through deterministic BLZ re-encoding.</summary>
    public NdsProgramStorageEncoding ProgramStorage { get; }

    /// <summary>Preserves the source program's verbatim prefix when <see cref="ProgramStorage"/> is BLZ.</summary>
    public int UncompressedPrefixLength { get; }

    /// <summary>Reports whether this policy contains enough verified key and table information to repair changes.</summary>
    public bool CanRegenerate => _hmacKey is not null && _unrepairableReason is null;

    /// <summary>Explains which source invariant prevented an imported policy from acquiring repair credentials.</summary>
    internal string? UnrepairableReason => _unrepairableReason;

    /// <summary>Exposes copied key material only to the ARM9 content preparation stage.</summary>
    internal ReadOnlyMemory<byte> HmacKey => _hmacKey;

    /// <summary>Captures source storage conventions only after one embedded candidate reproduces all flagged records.</summary>
    internal static NdsOverlayAuthenticationBuildOptions FromImported(
        NdsOverlayAuthenticationTable table,
        ReadOnlySpan<byte> key) => new(
            table.RelativeOffset,
            table.ProgramStorage,
            table.ProgramStorage == NdsProgramStorageEncoding.Blz ? table.UncompressedPrefixLength : 0,
            key.ToArray(),
            unrepairableReason: null);

    /// <summary>Retains malformed or stale source state so a later build fails explicitly rather than silently stripping records.</summary>
    internal static NdsOverlayAuthenticationBuildOptions CreateUnrepairable(
        NdsOverlayAuthenticationTable table,
        string reason) => new(
            table.RelativeOffset,
            table.ProgramStorage,
            table.UncompressedPrefixLength,
            hmacKey: null,
            reason);
}
