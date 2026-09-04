namespace NdsForge;

/// <summary>Names the caller's explicit disposition of stored late-DS authentication during an image write.</summary>
public enum NdsDsAuthenticationWriteMode
{
    /// <summary>Retains original fields without validating them and reports that changed coverage may make them stale.</summary>
    PreserveStored,

    /// <summary>Clears the three late-DS HMAC fields, RSA signature, and their two declaration bits.</summary>
    Clear,

    /// <summary>Regenerates declared HMACs with caller credentials and either signs the final header or explicitly clears its signature.</summary>
    Regenerate,
}
