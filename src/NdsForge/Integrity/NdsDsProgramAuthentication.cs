using System.Security.Cryptography;

namespace NdsForge;

/// <summary>Streams canonical late-DS program coverage after explicitly verifying the secure-area representation.</summary>
internal static class NdsDsProgramAuthentication
{
    /// <summary>Normalizes only the fixed secure prefix while streaming every other declared program byte.</summary>
    internal static byte[] Compute(NdsImage image, ReadOnlySpan<byte> key, NdsKey1KeyTable? keyTable)
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

        if (keyTable is null)
        {
            throw new NotSupportedException(
                "A caller-supplied KEY1 table is required to establish the encrypted ARM9 authentication representation.");
        }

        byte[] prefix = new byte[NdsSecureArea.ByteLength];
        using Stream arm9Stream = image.OpenRead(arm9);
        arm9Stream.ReadExactly(prefix);
        NdsSecureAreaInspection inspection = NdsSecureArea.Inspect(
            prefix, image.Header.GameCode, image.Header.SecureAreaCrc, keyTable);
        prefix = inspection.State switch
        {
            NdsSecureAreaState.Encrypted => prefix,
            NdsSecureAreaState.Decrypted => NdsSecureArea.Encrypt(prefix, image.Header.GameCode, keyTable),
            _ => throw new NotSupportedException(
                $"The ARM9 secure-area state {inspection.State} cannot establish a verified encrypted authentication representation."),
        };

        using IncrementalHash hash = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA1, key);
        hash.AppendData(image.Header.RawData.Span[..0x160]);
        hash.AppendData(prefix);
        byte[] buffer = new byte[8192];
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
