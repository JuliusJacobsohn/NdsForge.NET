namespace NdsForge.Graphics.Images;

/// <summary>Chooses frequency-weighted seeds and refines them using bounded integer-only nearest-center iterations.</summary>
internal static class IndexedPaletteReduction
{
    /// <summary>Reduces a complete five-bit RGB histogram in a fixed numeric order, independent of dictionary enumeration.</summary>
    internal static List<ushort> Reduce(int[] counts, int limit)
    {
        ushort[] colors = Enumerable.Range(0, counts.Length).Where(i => counts[i] != 0).Select(i => (ushort)i).ToArray();
        ushort[] centers = new ushort[limit];
        int[] distances = Enumerable.Repeat(int.MaxValue, colors.Length).ToArray();
        for (int slot = 0; slot < limit; slot++)
        {
            long bestScore = -1;
            int best = 0;
            for (int i = 0; i < colors.Length; i++)
            {
                if (slot > 0) { distances[i] = Math.Min(distances[i], Distance(colors[i], centers[slot - 1])); }
                long score = (long)counts[colors[i]] * (slot == 0 ? 1 : distances[i]);
                if (score > bestScore) { bestScore = score; best = i; }
            }
            centers[slot] = colors[best];
        }

        // Each rounded weighted mean minimizes the assigned integer squared error.
        // Keeping empty centers and stopping after eight rounds bounds work for every input.
        long[] weights = new long[limit];
        long[] reds = new long[limit];
        long[] greens = new long[limit];
        long[] blues = new long[limit];
        for (int iteration = 0; iteration < 8; iteration++)
        {
            Array.Clear(weights); Array.Clear(reds); Array.Clear(greens); Array.Clear(blues);
            foreach (ushort color in colors)
            {
                int slot = Nearest(color, centers);
                long weight = counts[color];
                weights[slot] += weight;
                reds[slot] += weight * (color & 31);
                greens[slot] += weight * ((color >> 5) & 31);
                blues[slot] += weight * ((color >> 10) & 31);
            }
            bool changed = false;
            for (int slot = 0; slot < limit; slot++)
            {
                long weight = weights[slot];
                if (weight == 0) { continue; }
                int red = (int)((reds[slot] + (weight / 2)) / weight);
                int green = (int)((greens[slot] + (weight / 2)) / weight);
                int blue = (int)((blues[slot] + (weight / 2)) / weight);
                ushort color = (ushort)(red | (green << 5) | (blue << 10));
                changed |= color != centers[slot];
                centers[slot] = color;
            }
            if (!changed) { break; }
        }
        var result = new List<ushort>(limit);
        foreach (ushort color in centers) { if (!result.Contains(color)) { result.Add(color); } }
        return result;
    }

    /// <summary>Returns the first closest palette index, preserving explicit palette order on distance ties.</summary>
    internal static int Nearest(ushort color, ReadOnlySpan<ushort> palette)
    {
        int best = 0;
        int bestDistance = int.MaxValue;
        for (int i = 0; i < palette.Length; i++)
        {
            int distance = Distance(color, palette[i]);
            if (distance < bestDistance) { best = i; bestDistance = distance; }
        }
        return best;
    }

    /// <summary>Calculates squared Euclidean distance with equal channel weights in five-bit RGB space.</summary>
    private static int Distance(ushort left, ushort right)
    {
        int red = (left & 31) - (right & 31);
        int green = ((left >> 5) & 31) - ((right >> 5) & 31);
        int blue = ((left >> 10) & 31) - ((right >> 10) & 31);
        return (red * red) + (green * green) + (blue * blue);
    }
}
