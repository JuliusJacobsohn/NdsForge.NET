namespace NdsForge;

/// <summary>Identifies DSi application, launcher, authentication, and development capabilities.</summary>
[Flags]
public enum NdsDsiApplicationFeatures
{
    /// <summary>No application capability is declared.</summary>
    None = 0,
    /// <summary>Use DSi touchscreen and sound-controller behavior.</summary>
    DsiPeripheralMode = 1 << 0,
    /// <summary>Require acceptance of the declared EULA version.</summary>
    RequiresEula = 1 << 1,
    /// <summary>Load custom banner data from title storage.</summary>
    UsesExternalBanner = 1 << 2,
    /// <summary>Show the Nintendo Wi-Fi Connection launcher indicator.</summary>
    ShowsNetworkIcon = 1 << 3,
    /// <summary>Show the DS wireless launcher indicator.</summary>
    ShowsWirelessIcon = 1 << 4,
    /// <summary>Authenticate the icon and title banner data.</summary>
    AuthenticatesBanner = 1 << 5,
    /// <summary>Authenticate program data and the extended header.</summary>
    AuthenticatesPrograms = 1 << 6,
    /// <summary>The image uses development-application behavior, including development modcrypt derivation.</summary>
    DevelopmentApplication = 1 << 7,
}
