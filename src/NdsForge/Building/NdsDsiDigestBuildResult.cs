namespace NdsForge;

/// <summary>Couples generated hierarchical table bytes with the master HMAC embedded in the extended header.</summary>
/// <param name="SectorHashes">Twenty-byte HMAC entries covering NTR sectors followed by TWL sectors.</param>
/// <param name="BlockHashes">Twenty-byte HMAC entries covering configured groups in <paramref name="SectorHashes"/>.</param>
/// <param name="MasterHmac">HMAC-SHA1 over all block-table bytes.</param>
internal sealed record NdsDsiDigestBuildResult(
    byte[] SectorHashes,
    byte[] BlockHashes,
    byte[] MasterHmac);
