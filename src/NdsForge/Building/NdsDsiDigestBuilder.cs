using System.Security.Cryptography;

namespace NdsForge;

/// <summary>Generates the two-level DSi content HMAC hierarchy from already committed image bytes.</summary>
internal static class NdsDsiDigestBuilder
{
    /// <summary>
    /// Hashes NTR sectors followed by TWL sectors, groups those entries into block HMACs, and authenticates the
    /// complete block table for storage in the extended header.
    /// </summary>
    /// <param name="image">Readable, seekable build destination containing finalized covered content.</param>
    /// <param name="layout">Covered regions and table lengths computed from the same digest options.</param>
    /// <param name="options">Sector size and second-level grouping policy.</param>
    /// <param name="key">Explicit copied HMAC-SHA1 key.</param>
    /// <param name="cancellationToken">Cancels potentially large sequential hashing.</param>
    /// <returns>Both table payloads and their master HMAC.</returns>
    public static async ValueTask<NdsDsiDigestBuildResult> BuildAsync(
        Stream image,
        NdsImageBuildLayout layout,
        NdsDsiDigestOptions options,
        ReadOnlyMemory<byte> key,
        CancellationToken cancellationToken)
    {
        if (layout.SectorHashTable.Length > Array.MaxLength || layout.BlockHashTable.Length > Array.MaxLength)
        {
            throw new IOException("DSi digest tables are too large to materialize safely.");
        }

        byte[] sectorHashes = new byte[layout.SectorHashTable.Length];
        int hashOffset = 0;
        hashOffset = await AppendRegionSectorsAsync(
            image,
            layout.NtrDigest,
            options.SectorSize,
            key,
            sectorHashes,
            hashOffset,
            cancellationToken).ConfigureAwait(false);
        hashOffset = await AppendRegionSectorsAsync(
            image,
            layout.TwlDigest,
            options.SectorSize,
            key,
            sectorHashes,
            hashOffset,
            cancellationToken).ConfigureAwait(false);
        if (hashOffset != sectorHashes.Length)
        {
            throw new InvalidDataException("DSi sector hash count disagrees with the planned table length.");
        }

        byte[] blockHashes = BuildBlockHashes(sectorHashes, options.BlockSectorCount, key.Span);
#pragma warning disable CA5350 // DSi digest tables mandate HMAC-SHA1; the key policy is explicit in the build recipe.
        byte[] masterHmac = HMACSHA1.HashData(key.Span, blockHashes);
#pragma warning restore CA5350
        return new(sectorHashes, blockHashes, masterHmac);
    }

    /// <summary>Streams one covered interval as independently authenticated sectors into a shared table buffer.</summary>
    /// <param name="image">Build destination positioned internally for each read.</param>
    /// <param name="region">Covered NTR or TWL interval.</param>
    /// <param name="sectorSize">Maximum bytes in one HMAC input.</param>
    /// <param name="key">Explicit HMAC key.</param>
    /// <param name="destination">Complete first-level table.</param>
    /// <param name="hashOffset">Next 20-byte output slot, shared across NTR then TWL.</param>
    /// <param name="cancellationToken">Cancels reads and hashing.</param>
    /// <returns>The next unused table offset.</returns>
    private static async ValueTask<int> AppendRegionSectorsAsync(
        Stream image,
        NdsRegion region,
        int sectorSize,
        ReadOnlyMemory<byte> key,
        byte[] destination,
        int hashOffset,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[Math.Min(sectorSize, 64 * 1024)];
        long regionOffset = 0;
        while (regionOffset < region.Length)
        {
#pragma warning disable CA5350 // DSi content sectors are specified as HMAC-SHA1 rather than a modern configurable algorithm.
            using IncrementalHash hash = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA1, key.Span);
#pragma warning restore CA5350
            long sectorRemaining = Math.Min(sectorSize, region.Length - regionOffset);
            image.Position = checked(region.Offset + regionOffset);
            while (sectorRemaining > 0)
            {
                int count = (int)Math.Min(buffer.Length, sectorRemaining);
                await image.ReadExactlyAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                hash.AppendData(buffer, 0, count);
                sectorRemaining -= count;
                regionOffset += count;
            }

            if (!hash.TryGetHashAndReset(destination.AsSpan(hashOffset, 20), out int written) || written != 20)
            {
                throw new CryptographicException("The platform HMAC provider did not produce a 20-byte SHA-1 result.");
            }

            hashOffset += 20;
        }

        return hashOffset;
    }

    /// <summary>Authenticates configured groups of consecutive sector-table entries.</summary>
    /// <param name="sectorHashes">Complete first-level table.</param>
    /// <param name="blockSectorCount">Maximum 20-byte entries per block.</param>
    /// <param name="key">Explicit HMAC key.</param>
    /// <returns>Contiguous 20-byte block HMAC entries.</returns>
    private static byte[] BuildBlockHashes(
        ReadOnlySpan<byte> sectorHashes,
        int blockSectorCount,
        ReadOnlySpan<byte> key)
    {
        int blockInputSize = checked(blockSectorCount * 20);
        int blockCount = checked((sectorHashes.Length + blockInputSize - 1) / blockInputSize);
        byte[] blockHashes = new byte[checked(blockCount * 20)];
        for (int index = 0; index < blockCount; index++)
        {
            int offset = checked(index * blockInputSize);
            ReadOnlySpan<byte> input = sectorHashes.Slice(offset, Math.Min(blockInputSize, sectorHashes.Length - offset));
#pragma warning disable CA5350 // DSi block-table entries are specified as HMAC-SHA1.
            HMACSHA1.HashData(key, input, blockHashes.AsSpan(index * 20, 20));
#pragma warning restore CA5350
        }

        return blockHashes;
    }
}
