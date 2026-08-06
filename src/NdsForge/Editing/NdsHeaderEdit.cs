using System.Text;

namespace NdsForge;

/// <summary>Collects validated mutable identity and card-control header fields.</summary>
public sealed class NdsHeaderEdit
{
    /// <summary>Anchors change detection to parsed values so no-op sessions remain byte-preserving even for damaged CRCs.</summary>
    private readonly NdsHeader _source;
    /// <summary>Seeds editable fields from a parsed header while deliberately excluding structural offsets and security bytes.</summary>
    /// <param name="source">Header whose developer-safe identity and card-control values become initial edits.</param>
    internal NdsHeaderEdit(NdsHeader source)
    {
        _source = source;
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

    /// <summary>Controls the padded 12-byte printable-ASCII application label; shorter values receive zero padding.</summary>
    public string Title { get; set; }

    /// <summary>Controls the exact four-character printable-ASCII product code used for title identity and region conventions.</summary>
    public string GameCode { get; set; }

    /// <summary>Controls the exact two-character printable-ASCII publisher code stored at header offset <c>0x10</c>.</summary>
    public string MakerCode { get; set; }

    /// <summary>Controls the publisher-defined one-byte software revision without changing any format version.</summary>
    public byte Version { get; set; }

    /// <summary>Controls the raw region byte whose defined bits depend on the image's DS or DSi unit code.</summary>
    public byte RegionCode { get; set; }

    /// <summary>Controls the complete boot-policy byte and preserves responsibility for reserved bits with the caller.</summary>
    public byte AutoStart { get; set; }

    /// <summary>Gets or sets normal card-control timing and flags.</summary>
    public uint NormalCardControl { get; set; }

    /// <summary>Gets or sets secure card-control timing and flags.</summary>
    public uint SecureCardControl { get; set; }

    /// <summary>Controls the raw 16-bit timeout applied to secure-area cartridge transfers.</summary>
    public ushort SecureTransferTimeout { get; set; }

    /// <summary>Compares every editable projection value without serializing or normalizing caller text.</summary>
    internal bool HasChanges =>
        Title != _source.Title ||
        GameCode != _source.GameCode ||
        MakerCode != _source.MakerCode ||
        Version != _source.Version ||
        RegionCode != _source.RegionCode ||
        AutoStart != _source.AutoStart ||
        NormalCardControl != _source.NormalCardControl ||
        SecureCardControl != _source.SecureCardControl ||
        SecureTransferTimeout != _source.SecureTransferTimeout;

    /// <summary>Validates and overlays only supported mutable fields onto a lossless header copy before CRC recalculation.</summary>
    /// <param name="header">Complete mutable common header prefix containing all destination offsets.</param>
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

    /// <summary>Encodes a fixed-width header identity field after enforcing printable ASCII and exact/minimum lengths.</summary>
    /// <param name="destination">Exact fixed-width field, cleared first to produce deterministic NUL padding.</param>
    /// <param name="value">Developer-supplied visible text.</param>
    /// <param name="minimumLength">Required visible characters; nonzero for fixed identifiers.</param>
    /// <param name="maximumLength">Field width and maximum visible characters.</param>
    /// <param name="propertyName">Public property named in validation failures.</param>
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
