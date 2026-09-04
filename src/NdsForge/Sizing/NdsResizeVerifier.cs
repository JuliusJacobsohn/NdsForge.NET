namespace NdsForge;

/// <summary>Checks both structural validity and complete byte preservation after a physical resize.</summary>
internal static class NdsResizeVerifier
{
    /// <summary>Compares bounded retained chunks, verifies every padding byte, and reparses the completed output.</summary>
    internal static async ValueTask VerifyAsync(
        NdsImage image, Stream destination, NdsImageResizeResult plan, byte paddingByte, CancellationToken cancellationToken)
    {
        if (destination.Length != plan.OutputLength) { throw new InvalidDataException("Resized output length does not match the requested plan."); }
        destination.Position = 0;
        long retainedLength = Math.Min(plan.InputLength, plan.OutputLength);
        using Stream source = image.OpenRead(new(0, retainedLength));
        byte[] expected = new byte[64 * 1024];
        byte[] actual = new byte[expected.Length];
        long remaining = retainedLength;
        while (remaining > 0)
        {
            int count = (int)Math.Min(expected.Length, remaining);
            await source.ReadExactlyAsync(expected.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
            await destination.ReadExactlyAsync(actual.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
            if (!expected.AsSpan(0, count).SequenceEqual(actual.AsSpan(0, count)))
            {
                throw new InvalidDataException("Resized output changed bytes that were required to be preserved.");
            }
            remaining -= count;
        }
        remaining = plan.OutputLength - retainedLength;
        while (remaining > 0)
        {
            int count = (int)Math.Min(actual.Length, remaining);
            await destination.ReadExactlyAsync(actual.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
            if (actual.AsSpan(0, count).IndexOfAnyExcept(paddingByte) >= 0)
            {
                throw new InvalidDataException("Resized output contains unexpected capacity-padding bytes.");
            }
            remaining -= count;
        }
        using NdsImage output = await NdsImage.OpenAsync(destination, leaveOpen: true, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!output.Validate().IsValid) { throw new InvalidDataException("Resized output failed structural or checksum validation."); }
    }
}
