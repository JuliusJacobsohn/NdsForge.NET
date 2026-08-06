namespace NdsForge;

internal static class NdsBannerParser
{
    public static NdsBanner? Parse(
        IImageDataSource source,
        uint offset,
        NdsReadOptions options)
    {
        if (offset == 0)
        {
            return null;
        }

        Span<byte> versionBytes = stackalloc byte[2];
        source.ReadExactly(offset, versionBytes);
        int size = GetValidatedSize(NdsBinary.ReadUInt16(versionBytes, 0), options);
        byte[] data = new byte[size];
        source.ReadExactly(offset, data);
        return new(data);
    }

    public static async ValueTask<NdsBanner?> ParseAsync(
        IImageDataSource source,
        uint offset,
        NdsReadOptions options,
        CancellationToken cancellationToken)
    {
        if (offset == 0)
        {
            return null;
        }

        byte[] versionBytes = new byte[2];
        await source.ReadExactlyAsync(offset, versionBytes, cancellationToken).ConfigureAwait(false);
        int size = GetValidatedSize(NdsBinary.ReadUInt16(versionBytes, 0), options);
        byte[] data = new byte[size];
        await source.ReadExactlyAsync(offset, data, cancellationToken).ConfigureAwait(false);
        return new(data);
    }

    private static int GetValidatedSize(ushort version, NdsReadOptions options)
    {
        int size = NdsBanner.GetSize(version);
        if (size > options.MaximumBannerBytes)
        {
            throw new InvalidDataException("The banner exceeds the configured parsing limit.");
        }

        return size;
    }
}

