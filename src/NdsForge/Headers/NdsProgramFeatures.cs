namespace NdsForge;

/// <summary>Identifies late-DS and DSi launcher, authentication, and development capabilities.</summary>
[Flags]
public enum NdsProgramFeatures
{
    /// <summary>No extended behavior is declared.</summary>
    None = 0,

    /// <summary>Uses the DSi touchscreen and sound-controller behavior.</summary>
    DsiTouchscreenAndSound = 1 << 0,

    /// <summary>Requires acceptance of the configured end-user license agreement.</summary>
    RequiresEula = 1 << 1,

    /// <summary>Uses an icon from saved banner data instead of the image banner.</summary>
    UsesSavedBannerIcon = 1 << 2,

    /// <summary>Requests the launcher Wi-Fi connection indicator.</summary>
    ShowsWifiIcon = 1 << 3,

    /// <summary>Requests the launcher local-wireless indicator.</summary>
    ShowsWirelessIcon = 1 << 4,

    /// <summary>Declares a banner HMAC in the extended header.</summary>
    AuthenticatesBanner = 1 << 5,

    /// <summary>Declares program and overlay HMACs together with an RSA header signature.</summary>
    AuthenticatesPrograms = 1 << 6,

    /// <summary>Marks development software rather than a retail application.</summary>
    DevelopmentApplication = 1 << 7,
}
