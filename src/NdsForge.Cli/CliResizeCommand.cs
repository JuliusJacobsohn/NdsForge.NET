using System.Globalization;

namespace NdsForge.Cli;

/// <summary>Exposes explicit source-preserving sizing without silently rebuilding metadata or deleting the input.</summary>
internal static class CliResizeCommand
{
    internal static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length < 4) { return Usage(); }
        NdsImageResizeMode? mode = args[3] switch
        {
            "preserve" => NdsImageResizeMode.Preserve,
            "trim" => NdsImageResizeMode.Trim,
            "pad" => NdsImageResizeMode.PadToDeviceCapacity,
            "exact" => NdsImageResizeMode.ExactLength,
            _ => null,
        };
        if (mode is null) { return Usage(); }
        long? length = null;
        byte padding = 255;
        bool overwrite = false;
        bool discard = false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 4; index < args.Length; index++)
        {
            string option = args[index];
            if (!seen.Add(option)) { return Usage(); }
            switch (option)
            {
                case "--overwrite": overwrite = true; break;
                case "--discard-trailing": discard = true; break;
                case "--length":
                    if (++index >= args.Length || !TryLength(args[index], out long parsed)) { return Usage(); }
                    length = parsed;
                    break;
                case "--padding-byte":
                    if (++index >= args.Length || args[index].Length != 2 ||
                        !byte.TryParse(args[index], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out padding)) { return Usage(); }
                    break;
                default: return Usage();
            }
        }
        if ((mode == NdsImageResizeMode.ExactLength) != length.HasValue ||
            (discard && mode is NdsImageResizeMode.Preserve or NdsImageResizeMode.PadToDeviceCapacity)) { return Usage(); }
        if (Path.GetFullPath(args[1]).Equals(Path.GetFullPath(args[2]), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new ArgumentException("Choose an output path distinct from the input image.");
        }
        using NdsImage image = await NdsImage.OpenAsync(args[1], cancellationToken: cancellationToken).ConfigureAwait(false);
        var options = new NdsImageResizeOptions
        {
            Mode = mode.Value,
            OutputLengthBytes = length,
            PaddingByte = padding,
            TrailingDataPolicy = discard ? NdsTrailingDataPolicy.Discard : NdsTrailingDataPolicy.RequirePadding,
            OverwriteDestination = overwrite,
        };
        NdsImageResizeResult result = await NdsImageResizer.WriteFileAsync(image, args[2], options, cancellationToken).ConfigureAwait(false);
        Console.WriteLine(FormattableString.Invariant($"Wrote {result.OutputLength} bytes to {Path.GetFullPath(args[2])}; input was {result.InputLength} bytes."));
        Console.WriteLine(FormattableString.Invariant($"Declared content ends at {image.SizeInfo.DeclaredContentEnd}; header capacity remains {image.SizeInfo.DeviceCapacityBytes} bytes."));
        if (result.RemovedData is { } removed) { Console.WriteLine(FormattableString.Invariant($"Omitted {removed.Length} trailing bytes from the output; all declared content was retained.")); }
        foreach (NdsDiagnostic diagnostic in result.Diagnostics) { Console.WriteLine($"{diagnostic.Severity} {diagnostic.Code}: {diagnostic.Message}"); }
        return 0;
    }

    private static bool TryLength(string text, out long value)
    {
        bool hex = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        return long.TryParse(hex ? text.AsSpan(2) : text.AsSpan(),
            hex ? NumberStyles.AllowHexSpecifier : NumberStyles.None, CultureInfo.InvariantCulture, out value) &&
            value is > 0 and <= 0x100000000L;
    }

    private static int Usage()
    {
        Console.Error.WriteLine("Usage: ndsforge resize <input.nds> <output.nds> <preserve|trim|pad|exact> [--length <bytes>] [--padding-byte <HH>] [--discard-trailing] [--overwrite]");
        Console.Error.WriteLine("Exact mode requires --length (decimal or 0x-prefixed hex); other modes reject it. Padding byte is two hex digits.");
        return 2;
    }
}
