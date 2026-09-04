namespace NdsForge;

/// <summary>Identifies defined DSi service, storage, certificate, and cartridge capabilities.</summary>
[Flags]
public enum NdsDsiAccessCapabilities : long
{
    /// <summary>No defined capability is requested.</summary>
    None = 0,
    /// <summary>Use of the common client key.</summary>
    CommonClientKey = 1 << 0,
    /// <summary>Permits cryptographic operations backed by system AES key slot B.</summary>
    AesSlotB = 1 << 1,
    /// <summary>Permits cryptographic operations backed by system AES key slot C.</summary>
    AesSlotC = 1 << 2,
    /// <summary>General SD-card device access.</summary>
    SdCard = 1 << 3,
    /// <summary>Permits application access to the console's internal NAND device.</summary>
    Nand = 1 << 4,
    /// <summary>Permission to power on the game card.</summary>
    GameCardPower = 1 << 5,
    /// <summary>Access to shared-data files.</summary>
    SharedDataFiles = 1 << 6,
    /// <summary>Launcher JPEG signing capability.</summary>
    LauncherJpegSigning = 1 << 7,
    /// <summary>Game-card access in original-DS mode.</summary>
    GameCardDsMode = 1 << 8,
    /// <summary>SSL client-certificate capability.</summary>
    SslClientCertificate = 1 << 9,
    /// <summary>User JPEG signing capability.</summary>
    UserJpegSigning = 1 << 10,
    /// <summary>Permits reading photographs managed by DSi system software.</summary>
    PhotoRead = 1 << 11,
    /// <summary>Permits writing photographs through DSi system services.</summary>
    PhotoWrite = 1 << 12,
    /// <summary>Permits reading files through the SD-card service interface.</summary>
    SdCardRead = 1 << 13,
    /// <summary>Permits writing files through the SD-card service interface.</summary>
    SdCardWrite = 1 << 14,
    /// <summary>Game-card save read access.</summary>
    GameCardSaveRead = 1 << 15,
    /// <summary>Game-card save write access.</summary>
    GameCardSaveWrite = 1 << 16,
    /// <summary>Use of the debugger common client key.</summary>
    DebuggerCommonClientKey = 1L << 31,
}
