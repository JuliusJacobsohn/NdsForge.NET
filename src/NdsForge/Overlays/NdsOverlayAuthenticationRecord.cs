namespace NdsForge;

/// <summary>Preserves one 20-byte Download Play HMAC associated with an ARM9 overlay-table position.</summary>
public sealed class NdsOverlayAuthenticationRecord
{
    /// <summary>Copies a bounded digest while retaining both positional and runtime overlay identities.</summary>
    internal NdsOverlayAuthenticationRecord(int overlayIndex, uint overlayId, ReadOnlySpan<byte> hmacSha1)
    {
        OverlayIndex = overlayIndex;
        OverlayId = overlayId;
        HmacSha1 = hmacSha1.ToArray();
    }

    /// <summary>Locates these digest bytes by the corresponding zero-based ARM9 overlay-table position.</summary>
    public int OverlayIndex { get; }

    /// <summary>Gets the runtime overlay identifier, which need not equal the table position or FAT file ID.</summary>
    public uint OverlayId { get; }

    /// <summary>Preserves the exact 20 stored bytes without treating their presence as proof of integrity.</summary>
    public ReadOnlyMemory<byte> HmacSha1 { get; }
}
