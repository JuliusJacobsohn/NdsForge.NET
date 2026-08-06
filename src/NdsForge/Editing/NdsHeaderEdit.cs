using System.Text;

namespace NdsForge;

/// <summary>Collects validated mutable identity and card-control header fields.</summary>
public sealed class NdsHeaderEdit
{
    internal NdsHeaderEdit(NdsHeader source)
    {
        Title = source.Title;
        GameCode = source.GameCode;
        MakerCode = source.MakerCode;
        Version = source.Version;
        RegionCode = source.RegionCode;
        AutoStart = source.AutoStart;
        NormalCardControl = source.NormalCardControl;
        SecureCardControl = source.SecureCardControl;
        SecureTransferTimeout = source.SecureTransferTimeout;
    }

    /// <summary>Gets or sets the ASCII application title of at most 12 characters.</summary>
    public string Title { get; set; }

    /// <summary>Gets or sets the exact four-character ASCII game code.</summary>
    public string GameCode { get; set; }

    /// <summary>Gets or sets the exact two-character ASCII maker code.</summary>
    public string MakerCode { get; set; }

    /// <summary>Gets or sets the application version.</summary>
    public byte Version { get; set; }

    /// <summary>Gets or sets the region-code byte.</summary>
    public byte RegionCode { get; set; }

    /// <summary>Gets or sets the autostart byte.</summary>
    public byte AutoStart { get; set; }

    /// <summary>Gets or sets normal card-control timing and flags.</summary>
    public uint NormalCardControl { get; set; }

    /// <summary>Gets or sets secure card-control timing and flags.</summary>
    public uint SecureCardControl { get; set; }

    /// <summary>Gets or sets the secure transfer timeout.</summary>
    public ushort SecureTransferTimeout { get; set; }

    internal void Apply(Span<byte> header)
    {
        WriteAscii(header.Slice(0x00, 12), Title, 0, 12, nameof(Title));
        WriteAscii(header.Slice(0x0C, 4), GameCode, 4, 4, nameof(GameCode));
        WriteAscii(header.Slice(0x10, 2), MakerCode, 2, 2, nameof(MakerCode));
        header[0x1D] = RegionCode;
        header[0x1E] = Version;
        header[0x1F] = AutoStart;
        NdsBinary.WriteUInt32(header, 0x60, NormalCardControl);
        NdsBinary.WriteUInt32(header, 0x64, SecureCardControl);
        NdsBinary.WriteUInt16(header, 0x6E, SecureTransferTimeout);
    }

    private static void WriteAscii(
        Span<byte> destination,
        string value,
        int minimumLength,
        int maximumLength,
        string propertyName)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length < minimumLength || value.Length > maximumLength ||
            value.Any(static character => character is < ' ' or > '~'))
        {
            throw new InvalidDataException(
                $"{propertyName} must contain {minimumLength} through {maximumLength} printable ASCII characters.");
        }

        destination.Clear();
        Encoding.ASCII.GetBytes(value, destination);
    }
}

