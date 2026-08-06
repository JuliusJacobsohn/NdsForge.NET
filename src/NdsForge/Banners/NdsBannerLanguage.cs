namespace NdsForge;

/// <summary>Identifies a localized Nintendo DS banner title slot.</summary>
public enum NdsBannerLanguage
{
    /// <summary>Occupies title slot zero and is available in every supported banner version.</summary>
    Japanese,

    /// <summary>Occupies title slot one and is available in every supported banner version.</summary>
    English,

    /// <summary>Occupies title slot two and is available in every supported banner version.</summary>
    French,

    /// <summary>Occupies title slot three and is available in every supported banner version.</summary>
    German,

    /// <summary>Occupies title slot four and is available in every supported banner version.</summary>
    Italian,

    /// <summary>Occupies title slot five and completes the six-language version-one layout.</summary>
    Spanish,

    /// <summary>Occupies slot six, introduced by banner version two and retained by later versions.</summary>
    Chinese,

    /// <summary>Occupies slot seven, introduced by banner version three and present in DSi animated banners.</summary>
    Korean,
}
