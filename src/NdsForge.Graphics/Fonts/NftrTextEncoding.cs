namespace NdsForge.Graphics.Fonts;

/// <summary>Identifies the character-code convention declared by an NFTR font.</summary>
public enum NftrTextEncoding
{
    /// <summary>UTF-8 character codes.</summary>
    Utf8 = 0,

    /// <summary>UTF-16 character codes.</summary>
    Utf16 = 1,

    /// <summary>Shift JIS character codes.</summary>
    ShiftJis = 2,

    /// <summary>Windows-1252 character codes.</summary>
    Windows1252 = 3,
}
