namespace NdsForge.Nitro.Text;

/// <summary>Preserves one non-message BMG section without imposing uncertain semantics.</summary>
public sealed class BmgAuxiliarySection
{
    internal BmgAuxiliarySection(string signature, ReadOnlyMemory<byte> data)
    {
        Signature = signature;
        Data = data;
    }

    /// <summary>Gets the exact four-character section signature.</summary>
    public string Signature { get; }

    /// <summary>Gets section bytes following the eight-byte section header.</summary>
    public ReadOnlyMemory<byte> Data { get; }
}
