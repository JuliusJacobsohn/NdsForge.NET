namespace NdsForge.Nitro.Compression;

/// <summary>Finds deterministic nearest-window LZ matches without coupling token layout to search policy.</summary>
internal static class NitroLzMatchFinder
{
    /// <summary>Searches at most 4096 preceding bytes and permits overlap represented by the original source.</summary>
    public static void Find(
        ReadOnlySpan<byte> data,
        int position,
        int maximumLength,
        out int bestLength,
        out int bestDisplacement)
    {
        bestLength = 0;
        bestDisplacement = 0;
        int availableLength = Math.Min(maximumLength, data.Length - position);
        int windowStart = Math.Max(0, position - 0x1000);
        for (int candidate = position - 1; candidate >= windowStart; candidate--)
        {
            int length = 0;
            while (length < availableLength && data[candidate + length] == data[position + length])
            {
                length++;
            }

            if (length > bestLength)
            {
                bestLength = length;
                bestDisplacement = position - candidate;
                if (length == availableLength)
                {
                    return;
                }
            }
        }
    }
}
