using System.Globalization;

namespace NdsForge.Cli;

/// <summary>Keeps structural output policy explicit and rejects duplicate or contradictory command options.</summary>
internal sealed record CliBuildArguments
{
    internal NdsImageBuildOptions BuildOptions { get; init; } = new();
    internal string? DsIntegrity { get; init; }
    internal string? DsiIntegrity { get; init; }

    internal static CliBuildArguments? Parse(string[] args)
    {
        if (args.Length < 3) { return null; }
        long? capacity = null;
        byte padding = 255;
        bool pad = false;
        bool overwrite = false;
        string? ds = null;
        string? dsi = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 3; index < args.Length; index++)
        {
            string option = args[index];
            if (!seen.Add(option)) { return null; }
            switch (option)
            {
                case "--overwrite": overwrite = true; break;
                case "--pad": pad = true; break;
                case "--capacity":
                    if (++index >= args.Length || !TryCapacity(args[index], out long parsed)) { return null; }
                    capacity = parsed;
                    break;
                case "--padding-byte":
                    if (++index >= args.Length || args[index].Length != 2 ||
                        !byte.TryParse(args[index], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out padding)) { return null; }
                    break;
                case "--ds-integrity":
                    if (++index >= args.Length || args[index] is not ("preserve" or "clear")) { return null; }
                    ds = args[index];
                    break;
                case "--dsi-integrity":
                    if (++index >= args.Length || args[index] is not ("clear" or "homebrew")) { return null; }
                    dsi = args[index];
                    break;
                default: return null;
            }
        }
        if (ds is not null && dsi is not null) { return null; }
        return new()
        {
            BuildOptions = new()
            {
                RequestedDeviceCapacityBytes = capacity,
                PadToDeviceCapacity = pad,
                PaddingByte = padding,
                OverwriteDestination = overwrite,
            },
            DsIntegrity = ds,
            DsiIntegrity = dsi,
        };
    }

    private static bool TryCapacity(string text, out long value)
    {
        bool hex = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        return long.TryParse(hex ? text.AsSpan(2) : text.AsSpan(),
            hex ? NumberStyles.AllowHexSpecifier : NumberStyles.None, CultureInfo.InvariantCulture, out value) &&
            value is >= 0x20000 and <= 0x100000000L && (value & (value - 1)) == 0;
    }
}
