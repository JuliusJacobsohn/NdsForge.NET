using System.Buffers.Binary;

namespace NdsForge;

/// <summary>
/// Reproduces ndstool's historical trainer layout by relocating ARM7, appending caller code and a header backup,
/// and patching boot addresses with the historical staged CRC correction. It is isolated because modern builds do not need it.
/// </summary>
public static class NdsLegacyArm7Hook
{
    /// <summary>Applies the compatibility transform to a complete DS-family image without mutating caller memory.</summary>
    /// <remarks>
    /// DSi-enhanced images retain their extended header and DSi-mode programs; the historical hook redirects only the
    /// original DS-mode ARM7 boot path, matching ndstool 1.50.3 behavior.
    /// </remarks>
    /// <param name="image">Complete original or previously hooked DS or DSi-enhanced image.</param>
    /// <param name="hook">Non-empty ARM7 machine code; zero padding extends it to a four-byte boundary.</param>
    /// <returns>Detached transformed bytes, introduced regions, and preserved CRC32.</returns>
    public static NdsLegacyArm7HookResult Apply(ReadOnlySpan<byte> image, ReadOnlySpan<byte> hook)
    {
        if (image.Length < 0x200)
        {
            throw new InvalidDataException("A legacy ARM7 hook requires at least a complete 512-byte DS header.");
        }

        if (hook.IsEmpty)
        {
            throw new ArgumentException("A legacy ARM7 hook payload cannot be empty.", nameof(hook));
        }

        byte[] originalHeader = image[..0x200].ToArray();
        RestoreHeaderBackupIfPresent(image, originalHeader);
        uint arm7Offset = ReadUInt32(originalHeader, 0x30);
        uint arm7Entry = ReadUInt32(originalHeader, 0x34);
        uint arm7Ram = ReadUInt32(originalHeader, 0x38);
        uint arm7Size = ReadUInt32(originalHeader, 0x3C);
        uint usedSize = ReadUInt32(originalHeader, 0x80);
        if ((ulong)arm7Offset + arm7Size > (ulong)image.Length || usedSize > image.Length)
        {
            throw new InvalidDataException("The source ARM7 region or declared used size lies outside the image.");
        }

        int alignedHookSize = checked((hook.Length + 3) & ~3);
        uint newArm7Offset = Align(checked(usedSize + 0x100u), 0x200);
        uint hookOffset = checked(newArm7Offset + arm7Size);
        uint headerBackupOffset = checked(hookOffset + (uint)alignedHookSize);
        uint newUsedSize = checked(headerBackupOffset + 0x200u);
        int outputLength = checked((int)Math.Max(image.Length, (long)newUsedSize + 4));
        var output = new byte[outputLength];
        image.CopyTo(output);
        byte[] relocatedArm7 = image.Slice(checked((int)arm7Offset), checked((int)arm7Size)).ToArray();
        var alignedHook = new byte[alignedHookSize];
        hook.CopyTo(alignedHook);
        int logicalLength = image.Length;

        NdsLegacyCrc32.ReplacePreservingCrc(output, ref logicalLength, checked((int)newArm7Offset), relocatedArm7);
        NdsLegacyCrc32.ReplacePreservingCrc(output, ref logicalLength, checked((int)hookOffset), alignedHook);
        NdsLegacyCrc32.ReplacePreservingCrc(output, ref logicalLength, checked((int)headerBackupOffset), originalHeader);
        byte[] patchedHeader = CreatePatchedHeader(
            originalHeader,
            headerBackupOffset,
            checked(arm7Ram + arm7Size + (uint)alignedHookSize),
            checked(arm7Entry + arm7Size),
            newArm7Offset,
            checked(arm7Size + (uint)alignedHookSize + 0x200u),
            newUsedSize);
        NdsLegacyCrc32.ReplacePreservingCrc(output, ref logicalLength, 0, patchedHeader, checked((int)newUsedSize));
        uint resultCrc = NdsLegacyCrc32.Calculate(output);

        return new(
            output,
            new(newArm7Offset, checked(arm7Size + (uint)alignedHookSize + 0x200u)),
            new(hookOffset, alignedHookSize),
            new(headerBackupOffset, 0x200),
            resultCrc);
    }

    /// <summary>Restores the original header embedded by a previous hook when its game code proves identity.</summary>
    /// <param name="image">Current complete image bytes.</param>
    /// <param name="header">Mutable current header replaced in place when a valid backup exists.</param>
    private static void RestoreHeaderBackupIfPresent(ReadOnlySpan<byte> image, Span<byte> header)
    {
        if (ReadUInt32(header, 0x160) == 0)
        {
            return;
        }

        uint backupOffset = ReadUInt32(header, 0x78);
        if ((ulong)backupOffset + 0x200 > (ulong)image.Length)
        {
            throw new InvalidDataException("A previously hooked image points its original header backup outside the file.");
        }

        ReadOnlySpan<byte> backup = image.Slice(checked((int)backupOffset), 0x200);
        if (backup.Slice(0x0C, 4).SequenceEqual(header.Slice(0x0C, 4)))
        {
            backup.CopyTo(header);
        }
    }

    /// <summary>Applies the trainer boot redirection while retaining every unrelated byte from the selected baseline header.</summary>
    /// <param name="original">Header selected as the unhooked baseline, including its original identity fields.</param>
    /// <param name="backupOffset">ROM position where hook code can recover that baseline header.</param>
    /// <param name="backupRamAddress">ARM7 address where the expanded load region places the baseline header.</param>
    /// <param name="arm7Entry">Entry address redirected to the appended trainer boundary.</param>
    /// <param name="arm7Offset">ROM position of the relocated original ARM7 program.</param>
    /// <param name="arm7Size">Expanded load length covering ARM7, trainer, and the header backup.</param>
    /// <param name="usedSize">Exclusive end of meaningful transformed data before the CRC correction word.</param>
    /// <returns>A detached 512-byte header with a newly valid common-header CRC16.</returns>
    private static byte[] CreatePatchedHeader(
        ReadOnlySpan<byte> original,
        uint backupOffset,
        uint backupRamAddress,
        uint arm7Entry,
        uint arm7Offset,
        uint arm7Size,
        uint usedSize)
    {
        byte[] header = original.ToArray();
        WriteUInt32(header, 0x78, backupOffset);
        WriteUInt32(header, 0x7C, backupRamAddress);
        WriteUInt32(header, 0x34, arm7Entry);
        WriteUInt32(header, 0x24, 0x027F_FE18);
        WriteUInt32(header, 0x18, 0xE59F_F004);
        WriteUInt32(header, 0x30, arm7Offset);
        WriteUInt32(header, 0x3C, arm7Size);
        WriteUInt32(header, 0x80, usedSize);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(0x15E), NdsChecksums.ComputeCrc16(header.AsSpan(0, 0x15E)));
        return header;
    }

    /// <summary>Reads a little-endian header word at a caller-proven in-bounds offset.</summary>
    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);

    /// <summary>Writes a little-endian header word without exposing internal binary helpers publicly.</summary>
    private static void WriteUInt32(Span<byte> data, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data[offset..], value);

    /// <summary>Rounds an unsigned offset upward under checked arithmetic.</summary>
    private static uint Align(uint value, uint alignment) => checked((value + alignment - 1) & ~(alignment - 1));
}
