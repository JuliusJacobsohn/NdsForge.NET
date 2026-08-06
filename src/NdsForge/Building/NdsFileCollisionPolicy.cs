namespace NdsForge;

/// <summary>Controls how a host-directory import handles a file path already present in the NitroFS recipe.</summary>
public enum NdsFileCollisionPolicy
{
    /// <summary>Rejects the complete import before any staged directory or file is applied.</summary>
    Fail,
    /// <summary>Retains the existing builder-owned payload and counts the staged host file as skipped.</summary>
    KeepExisting,
    /// <summary>Replaces the existing payload while preserving its logical NitroFS path.</summary>
    Replace,
}
