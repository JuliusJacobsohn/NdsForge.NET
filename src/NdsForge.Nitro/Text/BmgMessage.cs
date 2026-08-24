using System.Text;
using NdsForge.Nitro.Containers;

namespace NdsForge.Nitro.Text;

/// <summary>Models one BMG message with losslessly separated text and control parts.</summary>
public sealed class BmgMessage
{
    private readonly BmgEncoding _encoding;
    private readonly NitroByteOrder _byteOrder;

    internal BmgMessage(uint dataOffset, ReadOnlyMemory<byte> attributes, bool isNull,
        IReadOnlyList<BmgMessagePart> parts, BmgEncoding encoding, NitroByteOrder byteOrder)
    {
        DataOffset = dataOffset;
        Attributes = attributes;
        IsNull = isNull;
        Parts = Array.AsReadOnly(parts.ToArray());
        _encoding = encoding;
        _byteOrder = byteOrder;
    }

    /// <summary>Gets the DAT1-relative offset stored by INF1.</summary>
    public uint DataOffset { get; }

    /// <summary>Gets the message's opaque INF1 metadata bytes.</summary>
    public ReadOnlyMemory<byte> Attributes { get; }

    /// <summary>Gets whether the message points at DAT1's shared null entry.</summary>
    public bool IsNull { get; }

    /// <summary>Gets ordered text and control parts without discarding escape payloads.</summary>
    public IReadOnlyList<BmgMessagePart> Parts { get; }

    /// <summary>Decodes and concatenates text parts while omitting control sequences.</summary>
    /// <param name="shiftJisEncoding">
    /// Caller-supplied strict Shift JIS decoder when the bundle declares Shift JIS; ignored for other encodings.
    /// </param>
    /// <returns>The visible text surrounding any preserved control sequences.</returns>
    public string GetText(Encoding? shiftJisEncoding = null)
    {
        var result = new StringBuilder();
        foreach (BmgMessagePart part in Parts)
        {
            if (part.Kind != BmgMessagePartKind.Text) continue;
            result.Append(Decode(part.Data.Span, shiftJisEncoding));
        }
        return result.ToString();
    }

    private string Decode(ReadOnlySpan<byte> data, Encoding? shiftJisEncoding) => _encoding switch
    {
        BmgEncoding.Windows1252 => DecodeWindows1252(data),
        BmgEncoding.Utf16 => new UnicodeEncoding(
            bigEndian: _byteOrder == NitroByteOrder.BigEndian, byteOrderMark: false, throwOnInvalidBytes: true)
            .GetString(data),
        BmgEncoding.Utf8 => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(data),
        BmgEncoding.ShiftJis when shiftJisEncoding is not null => shiftJisEncoding.GetString(data),
        BmgEncoding.ShiftJis => throw new InvalidOperationException(
            "Shift JIS decoding requires an explicit Encoding instance; raw text bytes remain available in Parts."),
        _ => throw new InvalidOperationException("The BMG declares an unsupported encoding."),
    };

    private static string DecodeWindows1252(ReadOnlySpan<byte> data)
    {
        const string Special = "€\u0081‚ƒ„…†‡ˆ‰Š‹Œ\u008DŽ\u008F\u0090‘’“”•–—˜™š›œ\u009DžŸ";
        return string.Create(data.Length, data.ToArray(), static (target, source) =>
        {
            for (int index = 0; index < source.Length; index++)
            {
                byte value = source[index];
                target[index] = value is >= 0x80 and <= 0x9F ? Special[value - 0x80] : (char)value;
            }
        });
    }
}
