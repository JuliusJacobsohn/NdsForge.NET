namespace NdsForge.Nitro.Audio;

/// <summary>Implements the documented DS IMA step table, separate rounded delta terms, and directional saturation.</summary>
internal static class NitroAdpcmMath
{
    /// <summary>The eighty-nine format-defined positive step sizes indexed by the seven-bit ADPCM state.</summary>
    private static ReadOnlySpan<int> Steps =>
    [
        7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31, 34, 37, 41, 45,
        50, 55, 60, 66, 73, 80, 88, 97, 107, 118, 130, 143, 157, 173, 190, 209, 230,
        253, 279, 307, 337, 371, 408, 449, 494, 544, 598, 658, 724, 796, 876, 963,
        1060, 1166, 1282, 1411, 1552, 1707, 1878, 2066, 2272, 2499, 2749, 3024,
        3327, 3660, 4026, 4428, 4871, 5358, 5894, 6484, 7132, 7845, 8630, 9493,
        10442, 11487, 12635, 13899, 15289, 16818, 18500, 20350, 22385, 24623,
        27086, 29794, 32767,
    ];

    /// <summary>Advances one nibble. A positive zero delta may retain an initial -32768 even under DS clipping.</summary>
    internal static int Advance(int code, int predictor, ref int index, NitroAdpcmClipping clipping)
    {
        int step = Steps[index];
        int difference = step >> 3;
        if ((code & 1) != 0) { difference += step >> 2; }
        if ((code & 2) != 0) { difference += step >> 1; }
        if ((code & 4) != 0) { difference += step; }
        int magnitude = code & 7;
        index = Math.Clamp(index + (magnitude < 4 ? -1 : (magnitude - 3) * 2), 0, 88);
        return (code & 8) == 0
            ? Math.Min(32767, predictor + difference)
            : Math.Max(clipping == NitroAdpcmClipping.NintendoDs ? -32767 : -32768, predictor - difference);
    }

    /// <summary>Evaluates all sixteen legal next samples; equal absolute errors prefer the lower encoded nibble.</summary>
    internal static int ChooseCode(short sample, int predictor, int index, NitroAdpcmClipping clipping)
    {
        int bestCode = 0;
        int bestError = int.MaxValue;
        for (int code = 0; code < 16; code++)
        {
            int candidateIndex = index;
            int candidate = Advance(code, predictor, ref candidateIndex, clipping);
            int error = Math.Abs(candidate - sample);
            if (error < bestError) { bestCode = code; bestError = error; }
        }
        return bestCode;
    }
}
