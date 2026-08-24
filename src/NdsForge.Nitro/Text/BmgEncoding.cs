namespace NdsForge.Nitro.Text;

/// <summary>Identifies the character encoding declared by a BMG message bundle.</summary>
public enum BmgEncoding
{
    /// <summary>No encoding is selected.</summary>
    None = 0,

    /// <summary>Windows-1252 single-byte text.</summary>
    Windows1252 = 1,

    /// <summary>UTF-16 text in the bundle's integer byte order.</summary>
    Utf16 = 2,

    /// <summary>Shift JIS text.</summary>
    ShiftJis = 3,

    /// <summary>UTF-8 text.</summary>
    Utf8 = 4,
}
