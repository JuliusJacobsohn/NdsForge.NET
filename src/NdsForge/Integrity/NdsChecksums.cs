namespace NdsForge;

/// <summary>Calculates checksums used by Nintendo DS image structures.</summary>
public static class NdsChecksums
{
    /// <summary>Calculates the CRC-16 used by Nintendo DS headers and banners.</summary>
    /// <param name="data">The bytes to checksum.</param>
    /// <param name="seed">The initial CRC value.</param>
    /// <returns>The calculated checksum.</returns>
    public static ushort ComputeCrc16(ReadOnlySpan<byte> data, ushort seed = ushort.MaxValue)
    {
        ushort crc = seed;
        foreach (byte value in data)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (ushort)(((crc & 1) == 0) ? crc >> 1 : (crc >> 1) ^ 0xA001);
            }
        }

        return crc;
    }
}

