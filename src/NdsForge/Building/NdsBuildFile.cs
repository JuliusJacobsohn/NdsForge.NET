namespace NdsForge;

/// <summary>
/// Holds one NitroFS payload while an image is being assembled, independently of any source ROM.
/// </summary>
/// <remarks>
/// Instances are owned by <see cref="NdsFileSystemBuilder"/>. The builder copies input bytes, so a
/// caller may reuse or modify its original buffer after adding the file without changing a future
/// image. Paths use the canonical form <c>/directory/name.ext</c> and are compared byte-for-byte
/// using ordinal semantics because NitroFS names are case-sensitive ASCII data.
/// </remarks>
public sealed class NdsBuildFile
{
    /// <summary>
    /// Creates builder-owned state after path validation and payload copying have completed.
    /// </summary>
    /// <param name="path">A validated absolute NitroFS path.</param>
    /// <param name="contents">The private payload buffer retained by the builder.</param>
    internal NdsBuildFile(string path, byte[] contents)
    {
        Path = path;
        Contents = contents;
    }

    /// <summary>
    /// Identifies the file in NitroFS using a leading slash and ASCII path segments.
    /// </summary>
    /// <remarks>
    /// This is a logical image path, not a host-filesystem path. It changes when the owning builder
    /// moves the file, which allows an existing reference to continue identifying the same payload.
    /// </remarks>
    public string Path { get; internal set; }

    /// <summary>
    /// Exposes the exact bytes that will be assigned a FAT entry when the image is laid out.
    /// </summary>
    /// <remarks>
    /// The memory is backed by a builder-owned copy and cannot be mutated through this API. Replacing
    /// a file creates new state rather than retaining caller-owned memory.
    /// </remarks>
    public ReadOnlyMemory<byte> Contents { get; internal set; }
}
