namespace NdsForge;

/// <summary>Describes whether a declared ARM9 Download Play authentication table could be resolved safely.</summary>
public enum NdsOverlayAuthenticationTableState
{
    /// <summary>The complete table is bounded inside the decoded ARM9 program.</summary>
    Complete,

    /// <summary>Authenticated overlay flags exist, but ARM9 has no recognized SDK footer.</summary>
    MissingFooter,

    /// <summary>The SDK footer does not point to an authentication table.</summary>
    MissingTablePointer,

    /// <summary>The pointer and required record count exceed the decoded ARM9 program.</summary>
    TableOutOfRange,
}
