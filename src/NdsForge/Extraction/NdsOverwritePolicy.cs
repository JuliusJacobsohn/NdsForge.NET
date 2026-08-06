namespace NdsForge;

/// <summary>Controls how extraction handles existing regular files.</summary>
public enum NdsOverwritePolicy
{
    /// <summary>Fail before replacing an existing file.</summary>
    Fail,

    /// <summary>Atomically replace an existing regular file where supported.</summary>
    Overwrite,

    /// <summary>Leave an existing regular file unchanged.</summary>
    Skip,
}

