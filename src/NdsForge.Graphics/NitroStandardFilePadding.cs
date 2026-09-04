namespace NdsForge.Graphics;

/// <summary>Validates optional zero fill between the last standard block and a four-byte-aligned declared file end.</summary>
internal static class NitroStandardFilePadding
{
    /// <summary>Accepts exact block coverage or at most three zero bytes that align the declared file length.</summary>
    internal static bool IsValid(ReadOnlySpan<byte> data, int blocksEnd, int declaredLength)
    {
        if (blocksEnd == declaredLength)
        {
            return true;
        }

        int length = declaredLength - blocksEnd;
        if (length is < 1 or > 3 || (declaredLength & 3) != 0 || blocksEnd < 0 || declaredLength > data.Length)
        {
            return false;
        }

        foreach (byte value in data.Slice(blocksEnd, length))
        {
            if (value != 0)
            {
                return false;
            }
        }

        return true;
    }
}
