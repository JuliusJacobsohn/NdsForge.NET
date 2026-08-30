using System.Security.Cryptography;

namespace NdsForge;

/// <summary>Streams canonical late-DS program coverage after explicitly verifying the secure-area representation.</summary>
internal static class NdsDsProgramAuthentication
{
    /// <summary>Normalizes only the fixed secure prefix while streaming every other declared program byte.</summary>
    internal static byte[] Compute(NdsImage image, ReadOnlySpan<byte> key, NdsKey1KeyTable? keyTable)
    {
        byte[] prefix = ReadEncryptedSecureArea(image, keyTable);
        return ComputeWithPrefix(image, key, prefix, image.Header.RawData.Span[..0x160]);
    }

    /// <summary>Obtains canonical encrypted bytes so a writer can finalize the secure-area CRC before hashing the header.</summary>
    internal static byte[] ReadEncryptedSecureArea(NdsImage image, NdsKey1KeyTable? keyTable)
    {
        NdsRegion arm9 = image.Header.Arm9.Data;
        NdsRegion arm7 = image.Header.Arm7.Data;
        RequireProgram(image, arm9);
        RequireProgram(image, arm7);
        if (arm9.Offset != NdsSecureArea.Offset || arm9.Length < NdsSecureArea.ByteLength)
        {
            throw new NotSupportedException(
                "Late-DS program authentication requires a canonical ARM9 at 0x4000 with a complete 16 KiB secure area.");
        }

        byte[] prefix = new byte[NdsSecureArea.ByteLength];
        using Stream arm9Stream = image.OpenRead(arm9);
        arm9Stream.ReadExactly(prefix);
        return NormalizeSecureArea(prefix, image.Header.GameCode, keyTable);
    }

    /// <summary>Checks caller KEY1 material before a build truncates its destination, without altering input bytes.</summary>
    internal static byte[] NormalizeSecureArea(ReadOnlySpan<byte> prefix, string gameCode, NdsKey1KeyTable? keyTable)
    {
        if (keyTable is null)
        {
            throw new NotSupportedException(
                "A caller-supplied KEY1 table is required to establish the encrypted ARM9 authentication representation.");
        }

        NdsSecureAreaInspection inspection = NdsSecureArea.Inspect(
            prefix, gameCode, 0, keyTable);
        return inspection.State switch
        {
            NdsSecureAreaState.Encrypted => prefix.ToArray(),
            NdsSecureAreaState.Decrypted => NdsSecureArea.Encrypt(prefix, gameCode, keyTable),
            _ => throw new NotSupportedException(
                $"The ARM9 secure-area state {inspection.State} cannot establish a verified encrypted authentication representation."),
        };
    }

    /// <summary>Hashes final header bytes and one already-established encrypted prefix with streamed program tails.</summary>
    internal static byte[] ComputeWithPrefix(
        NdsImage image, ReadOnlySpan<byte> key, ReadOnlySpan<byte> prefix, ReadOnlySpan<byte> headerPrefix)
    {
        NdsRegion arm9 = image.Header.Arm9.Data;
        NdsRegion arm7 = image.Header.Arm7.Data;
        RequireProgram(image, arm9);
        RequireProgram(image, arm7);
        if (prefix.Length != NdsSecureArea.ByteLength || headerPrefix.Length != 0x160 || arm9.Length < prefix.Length)
        {
            throw new InvalidDataException("Late-DS program authentication requires complete header and encrypted secure-area prefixes.");
        }

        using IncrementalHash hash = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA1, key);
        hash.AppendData(headerPrefix);
        hash.AppendData(prefix);
        byte[] buffer = new byte[8192];
        using Stream arm9Stream = image.OpenRead(new(arm9.Offset + prefix.Length, arm9.Length - prefix.Length));
        Append(hash, arm9Stream, buffer);
        using Stream arm7Stream = image.OpenRead(arm7);
        Append(hash, arm7Stream, buffer);
        return hash.GetHashAndReset();
    }

    /// <summary>Checks the entire declared input before allocating the fixed normalization prefix.</summary>
    private static void RequireProgram(NdsImage image, NdsRegion region)
    {
        if (region.Offset < 0 || region.Length <= 0 || region.Offset > image.Length - region.Length)
        {
            throw new InvalidDataException("Late-DS program authentication requires nonempty, physically bounded ARM9 and ARM7 programs.");
        }
    }

    /// <summary>Appends the remaining bounded component using one reused fixed-size buffer.</summary>
    private static void Append(IncrementalHash hash, Stream stream, byte[] buffer)
    {
        int count;
        while ((count = stream.Read(buffer)) != 0)
        {
            hash.AppendData(buffer.AsSpan(0, count));
        }
    }
}
