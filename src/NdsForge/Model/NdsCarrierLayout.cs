namespace NdsForge;

/// <summary>Describes carrier-only material without conflating it with unit-code execution semantics.</summary>
public abstract class NdsCarrierLayout
{
    /// <summary>Retains already-owned opaque bytes and immutable detection findings.</summary>
    internal NdsCarrierLayout(byte[] postHeaderData, IReadOnlyList<NdsDiagnostic> diagnostics)
    {
        PostHeaderData = postHeaderData;
        Diagnostics = diagnostics;
    }

    /// <summary>Identifies cartridge, digital title, or unresolved storage semantics.</summary>
    public abstract NdsImageCarrier Kind { get; }

    /// <summary>Preserves available bytes from the reserved 0x1000–0x3FFF region, when distinct from declared components.</summary>
    public ReadOnlyMemory<byte> PostHeaderData { get; }

    /// <summary>Locates the retained opaque bytes, or returns null when that region is not independently reserved.</summary>
    public NdsRegion? PostHeaderRegion => PostHeaderData.IsEmpty ? null : new(0x1000, PostHeaderData.Length);

    /// <summary>Provides explicit carrier ambiguity and malformed-layout findings independently of cryptographic trust.</summary>
    public IReadOnlyList<NdsDiagnostic> Diagnostics { get; }
}

/// <summary>Describes cartridge storage, including optional DSi access-boundary declarations.</summary>
public sealed class NdsCartridgeLayout : NdsCarrierLayout
{
    /// <summary>Decodes cartridge protocol boundaries independently of meaningful and physical image sizes.</summary>
    internal NdsCartridgeLayout(NdsHeader header, byte[] data, IReadOnlyList<NdsDiagnostic> diagnostics) : base(data, diagnostics)
    {
        NtrRegionEnd = NdsBinary.ReadUInt16(header.RawData.Span, 0x90) * 0x80000L;
        TwlRegionStart = NdsBinary.ReadUInt16(header.RawData.Span, 0x92) * 0x80000L;
    }

    /// <inheritdoc />
    public override NdsImageCarrier Kind => NdsImageCarrier.Cartridge;

    /// <summary>Gets the decoded NTR protocol boundary; zero means no boundary is declared.</summary>
    public long NtrRegionEnd { get; }

    /// <summary>Gets the decoded TWL protocol boundary; zero means no boundary is declared.</summary>
    public long TwlRegionStart { get; }
}

/// <summary>Describes a digital executable SRL, including title identity and opaque carrier material.</summary>
public sealed class NdsDigitalSrlLayout : NdsCarrierLayout
{
    /// <summary>Retains a recognized executable NAND title identity without asserting publisher authenticity.</summary>
    internal NdsDigitalSrlLayout(ulong titleId, byte[] data, IReadOnlyList<NdsDiagnostic> diagnostics) : base(data, diagnostics)
    {
        TitleId = titleId;
    }

    /// <inheritdoc />
    public override NdsImageCarrier Kind => NdsImageCarrier.DigitalSrl;

    /// <summary>Combines the high category word and low product word identifying executable content in DSi title storage.</summary>
    public ulong TitleId { get; }
}

/// <summary>Retains material from an image whose carrier cannot be selected without resolving explicit diagnostics.</summary>
public sealed class NdsUnknownCarrierLayout : NdsCarrierLayout
{
    /// <summary>Retains bounded bytes while refusing to silently guess contradictory or unsupported storage semantics.</summary>
    internal NdsUnknownCarrierLayout(byte[] data, IReadOnlyList<NdsDiagnostic> diagnostics) : base(data, diagnostics) { }

    /// <inheritdoc />
    public override NdsImageCarrier Kind => NdsImageCarrier.Unknown;
}
