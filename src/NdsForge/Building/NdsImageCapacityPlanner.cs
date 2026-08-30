namespace NdsForge;

/// <summary>Applies explicit cartridge capacity only after every stored and authenticated region has been planned.</summary>
internal static class NdsImageCapacityPlanner
{
    /// <summary>Rejects contradictory requests before writing and separates content extent from capacity padding.</summary>
    /// <param name="builder">Recipe carrying the storage-carrier identity.</param>
    /// <param name="layout">Layout including authentication coverage and carrier reservations.</param>
    /// <param name="options">Explicit capacity and padding choices.</param>
    /// <param name="destination">Destination checked for its contiguous-array size limit.</param>
    /// <returns>A layout whose used regions remain unchanged.</returns>
    public static NdsImageBuildLayout Apply(
        NdsImageBuilder builder, NdsImageBuildLayout layout, NdsImageBuildOptions options, Stream destination)
    {
        if (builder.Carrier == NdsImageCarrier.DigitalSrl &&
            (options.RequestedDeviceCapacityBytes is not null || options.PadToDeviceCapacity))
        {
            throw new ArgumentException("Digital SRL output has no cartridge-capacity padding policy; retain compact output and its informational capacity byte.");
        }

        long minimum = NdsNandHeader.RequiredCapacity(builder, layout.PhysicalSize);
        long capacity = options.RequestedDeviceCapacityBytes ?? MinimumCapacity(minimum);
        if (capacity < minimum)
        {
            throw new ArgumentException("Requested device capacity cannot contain all planned content, authentication coverage, carrier reservations, and NAND partition boundaries.");
        }

        long physicalSize = options.PadToDeviceCapacity ? capacity : layout.PhysicalSize;
        if (destination is MemoryStream && physicalSize > Array.MaxLength)
        {
            throw new ArgumentException("The requested output exceeds contiguous-array limits; use a file or another seekable stream.");
        }

        return layout with { ContentSize = layout.PhysicalSize, PhysicalSize = physicalSize, DeviceCapacityBytes = capacity };
    }

    /// <summary>Finds the smallest power-of-two cartridge capacity containing an already bounded layout.</summary>
    /// <param name="length">Complete unpadded length.</param>
    /// <returns>At least 128 KiB, without adding any bytes to the output.</returns>
    private static long MinimumCapacity(long length)
    {
        long capacity = 0x20000;
        while (capacity < length) { capacity = checked(capacity * 2); }
        return capacity;
    }
}
