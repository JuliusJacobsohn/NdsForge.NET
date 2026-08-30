namespace NdsForge;

/// <summary>Requires recipe carrier selection to agree with executable title metadata before destination mutation.</summary>
internal static class NdsCarrierBuildValidator
{
    /// <summary>Rejects unresolved carriers and incompatible mixtures of cartridge and digital declarations.</summary>
    internal static void Validate(NdsImageBuilder builder)
    {
        uint category = (uint)((builder.DsiMetadata?.TitleId ?? 0) >> 32);
        bool digitalCategory = NdsCarrierLayoutParser.IsDigitalCategory(category);
        if (!builder.TwlReservedData.IsEmpty && (builder.Carrier != NdsImageCarrier.Cartridge || builder.Kind == NdsImageKind.NintendoDs))
        {
            throw new InvalidDataException("A TWL reservation requires a DSi cartridge recipe.");
        }
        if (builder.Carrier is not (NdsImageCarrier.Cartridge or NdsImageCarrier.DigitalSrl))
        {
            throw new InvalidDataException("A build requires an explicit cartridge or digital-SRL carrier.");
        }
        if ((builder.Carrier == NdsImageCarrier.DigitalSrl) != digitalCategory)
        {
            throw new InvalidDataException("The selected carrier and executable digital title category disagree.");
        }
        if (builder.Carrier == NdsImageCarrier.DigitalSrl && builder.Kind == NdsImageKind.NintendoDs)
        {
            throw new InvalidDataException("DS-mode digital SRL structural writing is not independently verified; use a byte-exact preservation copy.");
        }
    }
}
