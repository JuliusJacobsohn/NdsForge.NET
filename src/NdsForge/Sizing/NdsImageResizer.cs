namespace NdsForge;

/// <summary>Copies images with explicit physical sizing, without relocating components or altering header and authentication bytes.</summary>
public static class NdsImageResizer
{
    /// <summary>Writes retained bytes and optional padding to an independent readable, writable, seekable stream.</summary>
    /// <remarks>Source and destination must not share mutable storage, including through wrappers. Preflight failures leave output unchanged; I/O failures or cancellation during writing can leave partial stream output.</remarks>
    /// <param name="image">Live, immutable source image.</param>
    /// <param name="destination">Independent caller-owned output left open on return.</param>
    /// <param name="options">Explicit sizing and trailing-data choices, or complete preservation by default.</param>
    /// <param name="cancellationToken">Cancels preflight scanning, copying, padding, or verification.</param>
    /// <returns>The physical input/output lengths and affected trailing ranges.</returns>
    public static async ValueTask<NdsImageResizeResult> WriteAsync(
        NdsImage image, Stream destination, NdsImageResizeOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(destination);
        image.ValidateIndependentDestination(destination);
        if (!destination.CanRead || !destination.CanWrite || !destination.CanSeek)
        {
            throw new ArgumentException("Resize output must be readable, writable, and seekable.", nameof(destination));
        }
        options ??= NdsImageResizeOptions.Default;
        options.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        NdsImageResizeResult plan = await PlanAsync(image, options, cancellationToken).ConfigureAwait(false);
        if (destination is MemoryStream && plan.OutputLength > Array.MaxLength)
        {
            throw new ArgumentException("The output exceeds contiguous-array limits; use a file or another seekable stream.", nameof(destination));
        }

        destination.Position = 0;
        destination.SetLength(0);
        long retainedLength = Math.Min(plan.InputLength, plan.OutputLength);
        await image.CopyToAsync(new(0, retainedLength), destination, cancellationToken).ConfigureAwait(false);
        if (plan.AddedData is { } added)
        {
            byte[] buffer = new byte[64 * 1024];
            buffer.AsSpan().Fill(options.PaddingByte);
            long remaining = added.Length;
            while (remaining > 0)
            {
                int count = (int)Math.Min(buffer.Length, remaining);
                await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                remaining -= count;
            }
        }
        destination.SetLength(plan.OutputLength);
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        if (options.VerifyOutput)
        {
            await NdsResizeVerifier.VerifyAsync(image, destination, plan, options.PaddingByte, cancellationToken).ConfigureAwait(false);
        }
        destination.Position = plan.OutputLength;
        return plan;
    }

    /// <summary>Writes a temporary sibling and replaces the selected regular destination only after successful completion.</summary>
    /// <param name="image">Live source retained throughout copying and verification.</param>
    /// <param name="path">Caller-selected output path; reparse-point redirection is rejected.</param>
    /// <param name="options">Sizing, validation, and explicit overwrite choices.</param>
    /// <param name="cancellationToken">Cancels processing while leaving any existing destination unchanged.</param>
    /// <returns>The completed physical sizing report.</returns>
    public static ValueTask<NdsImageResizeResult> WriteFileAsync(
        NdsImage image, string path, NdsImageResizeOptions? options = null, CancellationToken cancellationToken = default) =>
        NdsResizePathWriter.WriteAsync(image, path, options ?? NdsImageResizeOptions.Default, cancellationToken);

    /// <summary>Computes the physical target and checks every byte that a padding-only shrink would remove.</summary>
    private static async ValueTask<NdsImageResizeResult> PlanAsync(
        NdsImage image, NdsImageResizeOptions options, CancellationToken cancellationToken)
    {
        NdsImageSizeInfo sizes = image.SizeInfo;
        var diagnostics = new List<NdsDiagnostic>(sizes.Diagnostics);
        if (options.Mode != NdsImageResizeMode.Preserve &&
            (sizes.Diagnostics.Any(static item => item.Severity == NdsDiagnosticSeverity.Error) ||
            image.CarrierLayout.Kind == NdsImageCarrier.Unknown ||
            image.CarrierLayout.Diagnostics.Any(static item => item.Severity == NdsDiagnosticSeverity.Error)))
        {
            throw new InvalidDataException("Resizing requires complete declared content and an unambiguous, valid carrier layout.");
        }
        if (options.VerifyOutput)
        {
            NdsValidationResult validation = image.Validate();
            if (!validation.IsValid)
            {
                throw new InvalidDataException($"Source validation failed: {string.Join("; ", validation.Diagnostics.Select(static item => item.Message))}");
            }
        }
        long length = options.Mode switch
        {
            NdsImageResizeMode.Preserve => sizes.PhysicalSize,
            NdsImageResizeMode.Trim => sizes.DeclaredContentEnd,
            NdsImageResizeMode.ExactLength => options.OutputLengthBytes!.Value,
            NdsImageResizeMode.PadToDeviceCapacity => GetExpansionCapacity(image),
            _ => throw new ArgumentException("Unsupported resize mode.", nameof(options)),
        };
        if (options.Mode != NdsImageResizeMode.Preserve && length < sizes.DeclaredContentEnd)
        {
            throw new ArgumentException("The requested length would remove declared content, protocol reservations, or authentication coverage.", nameof(options));
        }
        if (options.Mode == NdsImageResizeMode.ExactLength && image.CarrierLayout.Kind == NdsImageCarrier.Cartridge &&
            length > sizes.DeviceCapacityBytes)
        {
            throw new ArgumentException("The explicit length exceeds the unchanged header capacity; use a structural build to change capacity metadata.", nameof(options));
        }
        var plan = new NdsImageResizeResult(sizes.PhysicalSize, length, diagnostics.AsReadOnly());
        if (plan.RemovedData is { } removed)
        {
            if (options.TrailingDataPolicy == NdsTrailingDataPolicy.RequirePadding)
            {
                using Stream tail = image.OpenRead(removed);
                byte[] buffer = new byte[64 * 1024];
                long remaining = removed.Length;
                while (remaining > 0)
                {
                    int count = (int)Math.Min(buffer.Length, remaining);
                    await tail.ReadExactlyAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                    if (buffer.AsSpan(0, count).IndexOfAnyExcept(options.PaddingByte) >= 0)
                    {
                        throw new InvalidDataException("The removed interval contains unclassified non-padding bytes; preserve it or explicitly select trailing-data discard.");
                    }
                    remaining -= count;
                }
            }
            else
            {
                diagnostics.Add(new("NDS1580", NdsDiagnosticSeverity.Warning,
                    "Unclassified trailing bytes were explicitly discarded without asserting that they were padding.", removed));
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return plan;
    }

    /// <summary>Allows only genuine expansion to a representable cartridge capacity.</summary>
    private static long GetExpansionCapacity(NdsImage image)
    {
        if (image.CarrierLayout.Kind != NdsImageCarrier.Cartridge || image.SizeInfo.DeviceCapacityBytes is not long capacity ||
            capacity > 0x100000000L || capacity < image.Length)
        {
            throw new ArgumentException("Capacity expansion requires a cartridge capacity at least as large as the physical input and no greater than 4 GiB.");
        }
        return capacity;
    }
}
