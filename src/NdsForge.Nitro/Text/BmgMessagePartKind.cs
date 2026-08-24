namespace NdsForge.Nitro.Text;

/// <summary>Distinguishes ordinary encoded text from a BMG control sequence.</summary>
public enum BmgMessagePartKind
{
    /// <summary>The part contains bytes in the bundle's declared text encoding.</summary>
    Text,

    /// <summary>The part contains one typed, length-prefixed control sequence.</summary>
    Control,
}
