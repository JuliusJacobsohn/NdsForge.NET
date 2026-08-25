using System.Buffers.Binary;
using System.Security.Cryptography;
using NdsForge.Shared;

namespace NdsForge;

/// <summary>
/// Freezes profile-transformed programs, tables, and FAT payload order before physical layout begins.
/// Separating this snapshot from the public recipe makes compatibility quirks explicit without mutating
/// caller-owned definitions or making the offset planner responsible for semantic identity assignment.
/// </summary>
/// <param name="FileSystem">Serialized FNT plus visible files in their encoded ID order.</param>
/// <param name="Arm9Data">Physical ARM9 bytes excluding any trailing SDK footer.</param>
/// <param name="Arm9DeclaredLength">Length encoded in the header, which may include profile-specific rounding.</param>
/// <param name="Arm9TrailingData">Explicit and/or synthesized footer bytes written immediately after physical ARM9 data.</param>
/// <param name="Arm7Data">Physical ARM7 bytes.</param>
/// <param name="Arm7DeclaredLength">Length encoded in the header after profile-specific rounding.</param>
/// <param name="Arm9iData">DSi-mode ARM9 bytes, or empty memory for a DS recipe.</param>
/// <param name="Arm7iData">DSi-mode ARM7 bytes, or empty memory for a DS recipe.</param>
/// <param name="Allocations">Every FAT payload in final numeric File ID order.</param>
/// <param name="Arm9OverlayTable">Serialized ARM9 records in recipe insertion order.</param>
/// <param name="Arm7OverlayTable">Serialized ARM7 records in recipe insertion order.</param>
internal sealed record NdsImageBuildContent(
    NdsFileSystemBuildSnapshot FileSystem,
    ReadOnlyMemory<byte> Arm9Data,
    int Arm9DeclaredLength,
    ReadOnlyMemory<byte> Arm9TrailingData,
    ReadOnlyMemory<byte> Arm7Data,
    int Arm7DeclaredLength,
    ReadOnlyMemory<byte> Arm9iData,
    ReadOnlyMemory<byte> Arm7iData,
    ReadOnlyMemory<byte>[] Allocations,
    byte[] Arm9OverlayTable,
    byte[] Arm7OverlayTable);

/// <summary>Converts one logical build recipe into immutable bytes and File IDs governed by the selected profile.</summary>
internal static class NdsImageBuildContentPreparer
{
    /// <summary>
    /// Resolves hidden-versus-visible allocation ordering, program storage transformations, generated overlay
    /// records, and legacy footer behavior as one atomic snapshot.
    /// </summary>
    /// <param name="builder">Validated recipe whose byte-bearing members own stable copies.</param>
    /// <param name="options">Profile selecting deterministic or external-tool behavior.</param>
    /// <returns>All content inputs needed by layout and serialization, with no remaining mutable lookup.</returns>
    public static NdsImageBuildContent Prepare(NdsImageBuilder builder, NdsImageBuildOptions options)
    {
        bool ndstoolProfile = options.Profile == NdsImageBuildProfile.Ndstool1503;
        if (ndstoolProfile && builder.Kind != NdsImageKind.NintendoDs)
        {
            throw new InvalidDataException("The ndstool 1.50.3 compatibility profile predates DSi image creation.");
        }

        int arm9PrivateCount = builder.Arm9Overlays.Count(static overlay => overlay.HasPrivateAllocation);
        int arm7PrivateCount = builder.Arm7Overlays.Count(static overlay => overlay.HasPrivateAllocation);
        if (ndstoolProfile &&
            builder.Arm9Overlays.Concat(builder.Arm7Overlays).Any(static overlay => !overlay.HasPrivateAllocation))
        {
            throw new InvalidDataException(
                "The ndstool 1.50.3 build profile requires one private payload per Overlay record; its CLI cannot represent named-file links.");
        }

        if (ndstoolProfile && builder.Banner is not null && builder.Banner.RawData.Length != 0x840)
        {
            throw new InvalidDataException(
                "The ndstool 1.50.3 build profile supports only the 0x840-byte version-one Banner layout.");
        }

        int hiddenFileCount = ndstoolProfile ? checked(arm9PrivateCount + arm7PrivateCount) : 0;
        NdsFileSystemBuildSnapshot fileSystem = builder.FileSystem.BuildSnapshot(hiddenFileCount);
        ReadOnlyMemory<byte>[] allocations = CollectAllocations(builder, fileSystem, ndstoolProfile);
        int arm9PrivateBase = ndstoolProfile
            ? 0
            : checked(fileSystem.FirstFileId + fileSystem.FilesInIdOrder.Count);
        int arm7PrivateBase = checked(arm9PrivateBase + arm9PrivateCount);
        byte[] arm9OverlayTable = BuildOverlayTable(builder.Arm9Overlays, fileSystem, arm9PrivateBase);
        byte[] arm7OverlayTable = BuildOverlayTable(builder.Arm7Overlays, fileSystem, arm7PrivateBase);
        ReadOnlyMemory<byte> authenticatedArm9 = PrepareAuthenticatedArm9(builder, fileSystem, ndstoolProfile);
        (ReadOnlyMemory<byte> arm9Data, int arm9DeclaredLength) = PrepareProgram(
            builder.Arm9!,
            authenticatedArm9,
            isArm9: true,
            ndstoolProfile);
        (ReadOnlyMemory<byte> arm7Data, int arm7DeclaredLength) = PrepareProgram(
            builder.Arm7!,
            builder.Arm7!.Contents,
            isArm9: false,
            ndstoolProfile);
        ReadOnlyMemory<byte> arm9TrailingData = PrepareArm9TrailingData(
            builder.Arm9!,
            synthesizeNdstoolFooter: ndstoolProfile && arm9OverlayTable.Length > 0);
        ReadOnlyMemory<byte> arm9iData = builder.Arm9i?.Contents ?? ReadOnlyMemory<byte>.Empty;
        ReadOnlyMemory<byte> arm7iData = builder.Arm7i?.Contents ?? ReadOnlyMemory<byte>.Empty;
        return new(
            fileSystem,
            arm9Data,
            arm9DeclaredLength,
            arm9TrailingData,
            arm7Data,
            arm7DeclaredLength,
            arm9iData,
            arm7iData,
            allocations,
            arm9OverlayTable,
            arm7OverlayTable);
    }

    /// <summary>
    /// Applies physical program transformations while retaining the separately encoded header length. Version
    /// 1.50.3 prefixes raw ARM9 binaries with secure-area syscall stubs unless they already begin with that pattern.
    /// </summary>
    /// <param name="program">Logical executable bytes and processor identity.</param>
    /// <param name="contents">Authentication-adjusted bytes, or the unchanged definition payload.</param>
    /// <param name="isArm9">Enables the ARM9-only secure-area prefix.</param>
    /// <param name="ndstoolProfile">Selects legacy prefix and four-byte declared-size rounding.</param>
    /// <returns>Physical data plus the potentially rounded length written to the header.</returns>
    private static (ReadOnlyMemory<byte> Data, int DeclaredLength) PrepareProgram(
        NdsProgramDefinition program,
        ReadOnlyMemory<byte> contents,
        bool isArm9,
        bool ndstoolProfile)
    {
        if (!ndstoolProfile)
        {
            return (contents, contents.Length);
        }

        bool alreadyHasSecureStubs = isArm9 &&
            contents.Length >= 4 &&
            contents.Span[0] == 0xFF &&
            contents.Span[1] == 0xDE &&
            contents.Span[2] == 0xFF &&
            contents.Span[3] == 0xE7;
        int prefixLength = isArm9 && !alreadyHasSecureStubs ? 0x800 : 0;
        byte[] data = new byte[checked(contents.Length + prefixLength)];
        for (int offset = 0; offset < prefixLength; offset += 4)
        {
            data[offset] = 0xFF;
            data[offset + 1] = 0xDE;
            data[offset + 2] = 0xFF;
            data[offset + 3] = 0xE7;
        }

        contents.Span.CopyTo(data.AsSpan(prefixLength));
        int declaredLength = checked((data.Length + 3) & ~3);
        return (data, declaredLength);
    }

    /// <summary>Recomputes changed per-overlay records before layout freezes the ARM9 size and every downstream offset.</summary>
    private static ReadOnlyMemory<byte> PrepareAuthenticatedArm9(
        NdsImageBuilder builder,
        NdsFileSystemBuildSnapshot fileSystem,
        bool ndstoolProfile)
    {
        if (builder.Kind != NdsImageKind.NintendoDs ||
            !builder.Arm9Overlays.Any(static overlay => overlay.IsAuthenticated))
        {
            return builder.Arm9!.Contents;
        }

        ReadOnlySpan<byte> originalArm9 = builder.Arm9!.Contents.Span;
        bool ndstoolWouldPrefixProgram = ndstoolProfile &&
            (originalArm9.Length < 4 ||
                originalArm9[0] != 0xFF ||
                originalArm9[1] != 0xDE ||
                originalArm9[2] != 0xFF ||
                originalArm9[3] != 0xE7);
        if (ndstoolWouldPrefixProgram)
        {
            throw new InvalidDataException(
                "The ndstool 1.50.3 secure-area prefix would relocate decoded ARM9 overlay authentication records.");
        }

        NdsOverlayAuthenticationBuildOptions options = builder.Arm9OverlayAuthentication ??
            throw new InvalidDataException(
                "Authenticated ARM9 overlays require explicit overlay-authentication build options.");
        if (!options.CanRegenerate)
        {
            throw new InvalidDataException(
                $"ARM9 overlay authentication cannot be regenerated: {options.UnrepairableReason}");
        }

        NdsProgramDefinition program = builder.Arm9!;
        ValidateAuthenticationFooter(program, options.TableRelativeOffset);
        ReadOnlySpan<byte> stored = program.Contents.Span;
        byte[] decoded;
        if (options.ProgramStorage == NdsProgramStorageEncoding.Blz)
        {
            if (!BlzEngine.TryInspect(stored, out BlzEngineInfo sourceInfo))
            {
                throw new InvalidDataException("Overlay authentication expects BLZ ARM9 storage, but its envelope is invalid.");
            }

            if (sourceInfo.UncompressedPrefixLength < options.UncompressedPrefixLength)
            {
                throw new InvalidDataException(
                    "The stored ARM9 BLZ prefix is shorter than the authenticated build policy requires.");
            }

            decoded = BlzEngine.Decompress(stored, BlzEngine.DefaultMaximumDecodedLength);
        }
        else
        {
            decoded = stored.ToArray();
        }

        long tableLength = checked(builder.Arm9Overlays.Count * 20L);
        if (options.TableRelativeOffset > decoded.LongLength ||
            tableLength > decoded.LongLength - options.TableRelativeOffset)
        {
            throw new InvalidDataException(
                $"The overlay authentication table at decoded offset 0x{options.TableRelativeOffset:X} is not large enough for {builder.Arm9Overlays.Count} records.");
        }

        bool changed = false;
        for (int index = 0; index < builder.Arm9Overlays.Count; index++)
        {
            NdsOverlayDefinition overlay = builder.Arm9Overlays[index];
            if (!overlay.IsAuthenticated)
            {
                continue;
            }

            ReadOnlyMemory<byte> payload = ResolveOverlayPayload(overlay, fileSystem);
            Span<byte> record = decoded.AsSpan(checked((int)options.TableRelativeOffset + index * 20), 20);
#pragma warning disable CA5350 // Classic DS Download Play records are defined as HMAC-SHA1.
            byte[] calculated = HMACSHA1.HashData(options.HmacKey.Span, payload.Span);
#pragma warning restore CA5350
            if (!record.SequenceEqual(calculated))
            {
                calculated.CopyTo(record);
                changed = true;
            }
        }

        if (!changed)
        {
            return program.Contents;
        }

        if (options.ProgramStorage == NdsProgramStorageEncoding.Plain)
        {
            return decoded;
        }

        if (!BlzEngine.TryCompress(decoded, out byte[] encoded, options.UncompressedPrefixLength))
        {
            throw new InvalidDataException(
                "Updated ARM9 authentication records cannot be represented by a smaller BLZ stream.");
        }

        UpdateCompressedEndAddress(program, encoded);
        return encoded;
    }

    /// <summary>Requires the physical footer pointer to agree with the caller/imported decoded table identity.</summary>
    private static void ValidateAuthenticationFooter(NdsProgramDefinition program, uint tableRelativeOffset)
    {
        ReadOnlySpan<byte> footer = program.Footer.Span;
        if (footer.Length != 12 ||
            BinaryPrimitives.ReadUInt32LittleEndian(footer) != 0xDEC00621 ||
            BinaryPrimitives.ReadUInt32LittleEndian(footer[8..]) != tableRelativeOffset)
        {
            throw new InvalidDataException(
                "The ARM9 SDK footer is missing or disagrees with the overlay-authentication table offset.");
        }
    }

    /// <summary>Resolves private bytes directly and named links through the frozen filesystem snapshot.</summary>
    private static ReadOnlyMemory<byte> ResolveOverlayPayload(
        NdsOverlayDefinition overlay,
        NdsFileSystemBuildSnapshot fileSystem)
    {
        if (overlay.HasPrivateAllocation)
        {
            return overlay.Contents;
        }

        string path = overlay.EffectiveLinkedFilePath!;
        NdsBuildFile? file = fileSystem.FilesInIdOrder.FirstOrDefault(
            candidate => candidate.Path.Equals(path, StringComparison.Ordinal));
        return file?.Contents ??
            throw new InvalidDataException($"Overlay {overlay.Id} links missing NitroFS file '{path}'.");
    }

    /// <summary>Synchronizes the decoded SDK compressed-end word after deterministic BLZ output changes length.</summary>
    private static void UpdateCompressedEndAddress(NdsProgramDefinition program, byte[] encoded)
    {
        uint parametersOffset = BinaryPrimitives.ReadUInt32LittleEndian(program.Footer.Span[4..]);
        if (parametersOffset == 0 || parametersOffset > int.MaxValue - 0x18 ||
            !BlzEngine.TryInspect(encoded, out BlzEngineInfo info))
        {
            throw new InvalidDataException(
                "A recompressed authenticated ARM9 requires a bounded SDK parameter table in its verbatim prefix.");
        }

        int compressedEndOffset = checked((int)parametersOffset + 0x14);
        if (compressedEndOffset > info.UncompressedPrefixLength - sizeof(uint))
        {
            throw new InvalidDataException(
                "The ARM9 compressed-end field lies outside the BLZ verbatim prefix and cannot be repaired atomically.");
        }

        uint compressedEnd = checked(program.LoadAddress + (uint)encoded.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(encoded.AsSpan(compressedEndOffset), compressedEnd);
    }

    /// <summary>
    /// Preserves a caller-supplied SDK footer and appends the fixed legacy footer inserted by ndstool before an
    /// external ARM9 overlay table. The latter carries marker <c>0xDEC00621</c> and historical word <c>0xAD8</c>.
    /// </summary>
    /// <param name="program">ARM9 definition that may already carry a recognized footer.</param>
    /// <param name="synthesizeNdstoolFooter">Appends the footer emitted by ndstool 1.50.3 for ARM9 overlays.</param>
    /// <returns>Contiguous bytes written immediately after the unrounded physical ARM9 payload.</returns>
    private static ReadOnlyMemory<byte> PrepareArm9TrailingData(
        NdsProgramDefinition program,
        bool synthesizeNdstoolFooter)
    {
        if (!synthesizeNdstoolFooter)
        {
            return program.Footer;
        }

        byte[] trailing = new byte[checked(program.Footer.Length + 12)];
        program.Footer.Span.CopyTo(trailing);
        Span<byte> generated = trailing.AsSpan(program.Footer.Length, 12);
        BinaryPrimitives.WriteUInt32LittleEndian(generated, 0xDEC00621);
        BinaryPrimitives.WriteUInt32LittleEndian(generated[4..], 0x00000AD8);
        return trailing;
    }

    /// <summary>Orders hidden and visible allocations according to the profile's actual FAT identity convention.</summary>
    /// <param name="builder">Recipe supplying processor-separated private Overlay payloads.</param>
    /// <param name="fileSystem">Snapshot supplying visible payloads in encoded FNT order.</param>
    /// <param name="ndstoolProfile">Places hidden Overlay allocations first when reproducing version 1.50.3.</param>
    /// <returns>Immutable memory views whose array positions are their final FAT IDs.</returns>
    private static ReadOnlyMemory<byte>[] CollectAllocations(
        NdsImageBuilder builder,
        NdsFileSystemBuildSnapshot fileSystem,
        bool ndstoolProfile)
    {
        IEnumerable<ReadOnlyMemory<byte>> arm9 = builder.Arm9Overlays
            .Where(static overlay => overlay.HasPrivateAllocation)
            .Select(static overlay => overlay.Contents);
        IEnumerable<ReadOnlyMemory<byte>> arm7 = builder.Arm7Overlays
            .Where(static overlay => overlay.HasPrivateAllocation)
            .Select(static overlay => overlay.Contents);
        IEnumerable<ReadOnlyMemory<byte>> named = fileSystem.FilesInIdOrder.Select(static file => file.Contents);
        return (ndstoolProfile ? arm9.Concat(arm7).Concat(named) : named.Concat(arm9).Concat(arm7)).ToArray();
    }

    /// <summary>Serializes fixed Overlay records and resolves both hidden and named payload File IDs.</summary>
    /// <param name="overlays">One processor's definitions in desired table order.</param>
    /// <param name="fileSystem">Named payload order and its nonzero-compatible first File ID.</param>
    /// <param name="firstPrivateFileId">FAT index assigned to the first definition requiring a private allocation.</param>
    /// <returns>Complete table bytes whose length is exactly 32 times the definition count.</returns>
    private static byte[] BuildOverlayTable(
        IReadOnlyList<NdsOverlayDefinition> overlays,
        NdsFileSystemBuildSnapshot fileSystem,
        int firstPrivateFileId)
    {
        Dictionary<string, int> namedFileIds = fileSystem.FilesInIdOrder
            .Select((file, index) => (file.Path, FileId: checked(fileSystem.FirstFileId + index)))
            .ToDictionary(static item => item.Path, static item => item.FileId, StringComparer.Ordinal);
        byte[] data = new byte[checked(overlays.Count * 32)];
        int nextPrivateFileId = firstPrivateFileId;
        for (int index = 0; index < overlays.Count; index++)
        {
            NdsOverlayDefinition overlay = overlays[index];
            string? linkedPath = overlay.EffectiveLinkedFilePath;
            int fileId = linkedPath is null
                ? nextPrivateFileId++
                : namedFileIds.TryGetValue(linkedPath, out int linkedId)
                    ? linkedId
                    : throw new InvalidDataException($"Overlay {overlay.Id} links missing NitroFS file '{linkedPath}'.");
            Span<byte> entry = data.AsSpan(index * 32, 32);
            NdsBinary.WriteUInt32(entry, 0x00, overlay.Id);
            NdsBinary.WriteUInt32(entry, 0x04, overlay.LoadAddress);
            NdsBinary.WriteUInt32(entry, 0x08, overlay.RamSize);
            NdsBinary.WriteUInt32(entry, 0x0C, overlay.BssSize);
            NdsBinary.WriteUInt32(entry, 0x10, overlay.StaticInitializerStart);
            NdsBinary.WriteUInt32(entry, 0x14, overlay.StaticInitializerEnd);
            NdsBinary.WriteUInt32(entry, 0x18, checked((uint)fileId));
            NdsBinary.WriteUInt32(entry, 0x1C, overlay.CompressedSize | ((uint)overlay.Flags << 24));
        }

        return data;
    }
}
