namespace NdsForge.Nitro.Text;

/// <summary>Preserves one text span or typed control sequence within a BMG message.</summary>
public sealed class BmgMessagePart
{
    internal BmgMessagePart(BmgMessagePartKind kind, ReadOnlyMemory<byte> data, byte? controlCode,
        byte serializedLength)
    {
        Kind = kind;
        Data = data;
        ControlCode = controlCode;
        SerializedLength = serializedLength;
    }

    /// <summary>Gets whether this part is text or a control sequence.</summary>
    public BmgMessagePartKind Kind { get; }

    /// <summary>Gets encoded text bytes or the control payload following its type byte.</summary>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>Gets the control type, or <see langword="null"/> for text.</summary>
    public byte? ControlCode { get; }

    /// <summary>Gets the complete serialized control length, or zero for text.</summary>
    public byte SerializedLength { get; }
}
