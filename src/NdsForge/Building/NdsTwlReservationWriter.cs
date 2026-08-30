namespace NdsForge;

/// <summary>Preserves explicit carrier bytes or constructs the conventional dump mirror only when requested.</summary>
internal static class NdsTwlReservationWriter
{
    /// <summary>Writes exactly the planned reservation after all source bytes, including the header, are finalized.</summary>
    internal static async ValueTask WriteAsync(Stream image, NdsImageBuilder builder, NdsRegion? reservation,
        CancellationToken cancellationToken)
    {
        if (reservation is null) { return; }
        ReadOnlyMemory<byte> data = builder.TwlReservedData;
        if (data.IsEmpty)
        {
            byte[] generated = new byte[0x3000];
            image.Position = 0x8000;
            await image.ReadExactlyAsync(generated.AsMemory(0, 0x1000), cancellationToken).ConfigureAwait(false);
            generated.AsSpan(0, 0x1000).CopyTo(generated.AsSpan(0x1000));
            generated.AsSpan(0, 0x1000).CopyTo(generated.AsSpan(0x2000));
            data = generated;
        }
        image.Position = reservation.Value.Offset;
        await image.WriteAsync(data, cancellationToken).ConfigureAwait(false);
    }
}
