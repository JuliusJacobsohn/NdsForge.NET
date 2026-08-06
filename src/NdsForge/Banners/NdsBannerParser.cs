namespace NdsForge;

/// <summary>Reads the version prefix first so only the exact supported banner structure is allocated from an image.</summary>
internal static class NdsBannerParser
{
    /// <summary>Returns no banner for header offset zero; otherwise performs an exact version-sized synchronous read.</summary>
    /// <param name="source">Image source containing optional menu metadata.</param>
    /// <param name="offset">Absolute header value from offset <c>0x68</c>.</param>
    /// <param name="options">Allocation ceiling checked after version-to-size mapping.</param>
    /// <returns>A lossless banner model, or <see langword="null"/> when the image declares none.</returns>
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
        return NdsBanner.Parse(data);
    }

    /// <summary>Returns no banner for offset zero; otherwise performs cancellable prefix and full-structure reads.</summary>
    /// <param name="source">Image source containing optional menu metadata.</param>
    /// <param name="offset">Absolute header value from offset <c>0x68</c>.</param>
    /// <param name="options">Allocation ceiling checked after version-to-size mapping.</param>
    /// <param name="cancellationToken">Cancels before publishing a partially read banner.</param>
    /// <returns>A lossless banner model, or <see langword="null"/> when the image declares none.</returns>
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
        return NdsBanner.Parse(data);
    }

    /// <summary>Maps only known layouts and applies the caller's byte ceiling before allocating their payload.</summary>
    /// <param name="version">Raw little-endian banner version, including DSi value <c>0x0103</c>.</param>
    /// <param name="options">Resource policy applied even though supported versions have fixed sizes.</param>
    /// <returns>The exact byte length defined for the version.</returns>
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
