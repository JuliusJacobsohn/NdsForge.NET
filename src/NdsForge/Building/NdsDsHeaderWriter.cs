using System.Buffers.Binary;

namespace NdsForge;

/// <summary>Preserves late-DS metadata and finalizes every declared authentication field after physical layout is complete.</summary>
internal static class NdsDsHeaderWriter
{
    /// <summary>Identifies builds that need the verified leading-overlay allocation convention.</summary>
    internal static bool RequiresOverlayPrefix(NdsDsBuildMetadata? metadata) =>
        metadata?.Integrity?.Mode == NdsDsAuthenticationWriteMode.Regenerate &&
        (metadata.ProgramFeatures & NdsProgramFeatures.AuthenticatesPrograms) != 0;

    /// <summary>Validates the final allocation prefix and reserves physical bytes required by rounded authentication coverage.</summary>
    internal static NdsImageBuildLayout CompleteLayout(
        NdsImageBuilder builder, NdsImageBuildContent content, NdsImageBuildLayout layout, NdsImageBuildOptions options)
    {
        builder.DsMetadata?.Validate(builder, content);
        if (!RequiresOverlayPrefix(builder.DsMetadata)) { return layout; }
        if (layout.Arm9.Offset != NdsSecureArea.Offset || content.Arm9Data.Length < NdsSecureArea.ByteLength)
        {
            throw new InvalidDataException("Late-DS program authentication requires ARM9 at 0x4000 with a complete secure area.");
        }

        _ = NdsDsProgramAuthentication.NormalizeSecureArea(content.Arm9Data.Span[..NdsSecureArea.ByteLength],
            builder.GameCode, builder.DsMetadata!.Integrity!.SecureAreaKeyTable);
        uint[] fileIds = Enumerable.Range(0, builder.Arm9Overlays.Count)
            .Select(index => NdsBinary.ReadUInt32(content.Arm9OverlayTable, index * 32 + 0x18)).ToArray();
        IReadOnlyList<NdsRegion> regions = NdsDsAuthentication.GetOverlayHashRegions(long.MaxValue,
            layout.Arm9OverlayTable, layout.FileAllocationTable, layout.FileRegions, fileIds);
        long required = Math.Max(layout.PhysicalSize, regions.Count == 0 ? 0 : regions.Max(static region => region.End));
        long physicalSize = checked((required + options.FileAlignment - 1) & -(long)options.FileAlignment);
        if (physicalSize > uint.MaxValue)
        {
            throw new InvalidDataException("Rounded late-DS authentication coverage exceeds the image address space.");
        }

        return layout with { PhysicalSize = physicalSize };
    }

    /// <summary>Copies opaque extension bytes and relocates both SDK pointers without reusing absolute source addresses.</summary>
    internal static void WriteMetadata(
        Span<byte> header, NdsDsBuildMetadata metadata, NdsImageBuildLayout layout, NdsImageBuildContent content)
    {
        metadata.ExtensionTemplate.Span.CopyTo(header[0x180..0x1000]);
        header[0x1BF] = checked((byte)metadata.ProgramFeatures);
        WritePointer(header, 0x88, metadata.Arm9ParametersRelativeOffset, layout.Arm9, content.Arm9PrefixLength);
        WritePointer(header, 0x8C, metadata.Arm7ParametersRelativeOffset, layout.Arm7, 0);
    }

    /// <summary>Writes a component-relative SDK pointer after accounting for an explicitly inserted compatibility prefix.</summary>
    private static void WritePointer(Span<byte> header, int offset, uint? relative, NdsRegion program, int prefixLength) =>
        NdsBinary.WriteUInt32(header, offset, relative is uint value ? checked((uint)(program.Offset + prefixLength + value)) : 0);

    /// <summary>Finalizes the header using the actual written tables, stored payloads and padding, then checks an optional signer.</summary>
    internal static async ValueTask<IReadOnlyList<NdsDiagnostic>> FinalizeAsync(
        Stream destination, byte[] header, NdsDsIntegrityOptions? integrity, CancellationToken cancellationToken)
    {
        NdsProgramFeatures features = (NdsProgramFeatures)header[0x1BF];
        bool programs = (features & NdsProgramFeatures.AuthenticatesPrograms) != 0;
        bool banner = (features & NdsProgramFeatures.AuthenticatesBanner) != 0;
        if (integrity is null)
        {
            if (programs || banner)
            {
                throw new InvalidDataException("Changing late-DS authentication coverage requires an explicit write policy.");
            }

            return Array.Empty<NdsDiagnostic>();
        }

        if (integrity.Mode == NdsDsAuthenticationWriteMode.PreserveStored)
        {
            return programs || banner
                ? new[] { new NdsDiagnostic("NDS1540", NdsDiagnosticSeverity.Warning,
                    "Stored late-DS authentication was preserved without regeneration or verification; changed coverage may make it stale.") }
                : Array.Empty<NdsDiagnostic>();
        }

        ClearFields(header);
        if (integrity.Mode == NdsDsAuthenticationWriteMode.Clear)
        {
            header[0x1BF] &= 0x9F;
        }
        else
        {
            destination.Position = 0;
            using NdsImage image = await NdsImage.OpenAsync(destination, leaveOpen: true,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            integrity.Validate(features, image.Banner is not null);
            if (programs)
            {
                byte[] secureArea = NdsDsProgramAuthentication.ReadEncryptedSecureArea(image, integrity.SecureAreaKeyTable);
                NdsBinary.WriteUInt16(header, 0x6C, NdsChecksums.ComputeCrc16(secureArea));
                NdsBinary.WriteUInt16(header, 0x15E, NdsChecksums.ComputeCrc16(header.AsSpan(0, 0x15E)));
                NdsDsProgramAuthentication.ComputeWithPrefix(image, integrity.ProgramKey.Span, secureArea,
                    header.AsSpan(0, 0x160)).CopyTo(header, 0x378);
                if (image.Arm9Overlays.Count != 0)
                {
                    NdsDsAuthentication.ComputeOverlayHmac(image, integrity.ProgramKey.Span).CopyTo(header, 0x38C);
                }
            }

            if (banner)
            {
                NdsDsAuthentication.ComputeBannerHmac(image.Banner!, integrity.BannerKey.Span).CopyTo(header, 0x33C);
            }

            if (programs && integrity.SignatureProvider is not null)
            {
                integrity.SignatureProvider.SignHeader(header.AsSpan(0, 0xE00), header.AsSpan(0xF80, 128));
                if (!integrity.SignaturePublicKey!.VerifyHeader(header.AsSpan(0, 0xE00), header.AsSpan(0xF80, 128)))
                {
                    throw new InvalidDataException("The supplied late-DS signer produced a signature that failed its public-key verification.");
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        destination.Position = 0;
        await destination.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        return programs && integrity.Mode == NdsDsAuthenticationWriteMode.Regenerate && integrity.SignatureProvider is null
            ? new[] { new NdsDiagnostic("NDS1542", NdsDiagnosticSeverity.Warning,
                "Late-DS HMACs were regenerated, but the RSA signature was cleared because no signing authority was supplied.", new(0xF80, 128)) }
            : Array.Empty<NdsDiagnostic>();
    }

    /// <summary>Removes only late-DS authentication bytes, leaving unrelated reserved extension material lossless.</summary>
    private static void ClearFields(Span<byte> header)
    {
        header.Slice(0x33C, 20).Clear();
        header.Slice(0x378, 40).Clear();
        header.Slice(0xF80, 128).Clear();
    }
}
