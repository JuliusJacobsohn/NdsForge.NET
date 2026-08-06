using System.Buffers.Binary;
using System.Text;

namespace NdsForge;

/// <summary>
/// Serializes the common Nintendo DS cartridge header after layout has assigned every referenced region.
/// Keeping this operation separate prevents the layout planner from duplicating the format's offset tuples
/// and ensures both Nintendo logo and header checksums are computed only after all dependent fields are final.
/// </summary>
internal static class NdsImageHeaderWriter
{
    /// <summary>
    /// Creates the complete reserved header area, including compatibility-profile constants and checksums.
    /// Bytes not represented by the build recipe remain zero so deterministic builds cannot inherit stale
    /// metadata from an unrelated source image.
    /// </summary>
    /// <param name="builder">Validated recipe supplying cartridge identity, runtime addresses, and security metadata.</param>
    /// <param name="layout">Final physical regions whose offsets are encoded into the header.</param>
    /// <param name="content">Frozen Program and Banner bytes used by DSi authentication fields.</param>
    /// <param name="options">Header reservation and compatibility behavior selected for this build.</param>
    /// <param name="digestResult">Generated DSi hierarchy, or <see langword="null"/> when digests are absent.</param>
    /// <returns>A standalone header buffer whose length is exactly the requested reserved header size.</returns>
    public static byte[] Write(
        NdsImageBuilder builder,
        NdsImageBuildLayout layout,
        NdsImageBuildContent content,
        NdsImageBuildOptions options,
        NdsDsiDigestBuildResult? digestResult)
    {
        byte[] header = new byte[options.HeaderSize];
        WriteAscii(header.AsSpan(0x00, 12), builder.Title);
        WriteAscii(header.AsSpan(0x0C, 4), builder.GameCode);
        WriteAscii(header.AsSpan(0x10, 2), builder.MakerCode);
        header[0x12] = (byte)builder.Kind;
        header[0x13] = builder.EncryptionSeedSelect;
        header[0x14] = CalculateDeviceCapacity(layout.PhysicalSize);
        header[0x1D] = builder.RegionCode;
        header[0x1C] = builder.DsiMetadata?.DsiFlags ?? 0;
        header[0x1E] = builder.Version;
        header[0x1F] = builder.AutoStart;
        WriteProgram(header, 0x20, layout.Arm9, builder.Arm9!);
        WriteProgram(header, 0x30, layout.Arm7, builder.Arm7!);
        WriteRegion(header, 0x40, layout.FileNameTable);
        WriteRegion(header, 0x48, layout.FileAllocationTable);
        WriteRegion(header, 0x50, layout.Arm9OverlayTable);
        WriteRegion(header, 0x58, layout.Arm7OverlayTable);

        bool ndstoolProfile = options.Profile == NdsImageBuildProfile.Ndstool1503;
        NdsBinary.WriteUInt32(header, 0x60, ndstoolProfile ? 0x00406000U : builder.NormalCardControl);
        NdsBinary.WriteUInt32(header, 0x64, ndstoolProfile ? 0x20000000U : builder.SecureCardControl);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0x68), checked((uint)(layout.Banner?.Offset ?? 0)));
        NdsBinary.WriteUInt16(header, 0x6E, ndstoolProfile ? (ushort)0x051E : builder.SecureTransferTimeout);
        NdsBinary.WriteUInt32(header, 0x70, builder.Arm9AutoLoad);
        NdsBinary.WriteUInt32(header, 0x74, builder.Arm7AutoLoad);
        NdsBinary.WriteUInt32(header, 0x78, (uint)builder.SecureDisable);
        NdsBinary.WriteUInt32(header, 0x7C, (uint)(builder.SecureDisable >> 32));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0x80), checked((uint)layout.UsedSize));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0x84), checked((uint)options.HeaderSize));
        if (!builder.NintendoLogo.IsEmpty)
        {
            builder.NintendoLogo.Span.CopyTo(header.AsSpan(0xC0, 156));
        }

        if (builder.Kind != NdsImageKind.NintendoDs)
        {
            NdsDsiHeaderWriter.Write(header, builder, layout, content, digestResult);
        }

        BinaryPrimitives.WriteUInt16LittleEndian(
            header.AsSpan(0x15C),
            NdsChecksums.ComputeCrc16(header.AsSpan(0xC0, 156)));
        BinaryPrimitives.WriteUInt16LittleEndian(
            header.AsSpan(0x15E),
            NdsChecksums.ComputeCrc16(header.AsSpan(0, 0x15E)));
        if (builder.Kind != NdsImageKind.NintendoDs)
        {
            NdsDsiHeaderWriter.FinalizeSignature(header, builder.DsiMetadata!.Integrity);
        }

        return header;
    }

    /// <summary>
    /// Encodes one processor's storage region and runtime addresses as the four-word tuple defined by the
    /// common header. The stored size comes from the finalized region because compatibility profiles may pad
    /// or prefix the caller's original program bytes.
    /// </summary>
    /// <param name="header">Mutable common header prefix.</param>
    /// <param name="offset">Tuple offset: <c>0x20</c> for ARM9 or <c>0x30</c> for ARM7.</param>
    /// <param name="region">Final cartridge payload region, including profile-specific transformation.</param>
    /// <param name="program">Recipe object supplying entry and load addresses.</param>
    private static void WriteProgram(Span<byte> header, int offset, NdsRegion region, NdsProgramDefinition program)
    {
        NdsBinary.WriteUInt32(header, offset, checked((uint)region.Offset));
        NdsBinary.WriteUInt32(header, offset + 4, program.EntryAddress);
        NdsBinary.WriteUInt32(header, offset + 8, program.LoadAddress);
        NdsBinary.WriteUInt32(header, offset + 12, checked((uint)region.Length));
    }

    /// <summary>
    /// Encodes an offset/length pair for one metadata table. A zero-length absent table is deliberately
    /// represented by the layout's chosen offset, which allows compatibility profiles to reproduce their
    /// oracle's distinction between <c>(0, 0)</c> and an empty table placed at the current cursor.
    /// </summary>
    /// <param name="header">Mutable common header prefix.</param>
    /// <param name="offset">Byte offset of the destination start word.</param>
    /// <param name="region">Final region converted only after checked 32-bit validation.</param>
    private static void WriteRegion(Span<byte> header, int offset, NdsRegion region)
    {
        NdsBinary.WriteUInt32(header, offset, checked((uint)region.Offset));
        NdsBinary.WriteUInt32(header, offset + 4, checked((uint)region.Length));
    }

    /// <summary>
    /// Derives the smallest 128 KiB-scaled capacity exponent that contains the complete physical image.
    /// This reports a conventional power-of-two cartridge capacity without adding capacity padding to the file.
    /// </summary>
    /// <param name="physicalSize">Final destination length after file-alignment padding.</param>
    /// <returns>A header byte whose nominal capacity is at least the generated length.</returns>
    private static byte CalculateDeviceCapacity(long physicalSize)
    {
        byte exponent = 0;
        long capacity = 128 * 1024;
        while (capacity < physicalSize && exponent < 31)
        {
            exponent++;
            capacity <<= 1;
        }

        return exponent;
    }

    /// <summary>
    /// Writes prevalidated identity text into a fixed-width ASCII field. The destination originates from a
    /// zero-initialized header, so shorter titles retain the format's required NUL padding without extra writes.
    /// </summary>
    /// <param name="destination">Exact-width identity field inside the common header.</param>
    /// <param name="value">Printable ASCII text already bounded by recipe validation.</param>
    private static void WriteAscii(Span<byte> destination, string value) => Encoding.ASCII.GetBytes(value, destination);
}
