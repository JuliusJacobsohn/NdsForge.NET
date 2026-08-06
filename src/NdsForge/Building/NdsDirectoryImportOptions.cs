namespace NdsForge;

/// <summary>Bounds host-directory materialization and makes merge and link behavior explicit.</summary>
public sealed record NdsDirectoryImportOptions
{
    /// <summary>Returns a fresh conservative policy so one caller cannot mutate another import's defaults.</summary>
    public static NdsDirectoryImportOptions Default => new();

    /// <summary>Controls duplicate file paths while never allowing file-versus-directory structural ambiguity.</summary>
    public NdsFileCollisionPolicy CollisionPolicy { get; init; }

    /// <summary>Rejects host links by default because following them can escape the selected source tree.</summary>
    public NdsHostLinkPolicy LinkPolicy { get; init; }

    /// <summary>Limits staged payload count before any builder mutation occurs.</summary>
    public int MaximumFiles { get; init; } = 65_536;

    /// <summary>Limits the sum of host file lengths copied into memory for one transactional import.</summary>
    public long MaximumTotalBytes { get; init; } = 512L * 1024 * 1024;

    /// <summary>Rejects undefined policies and disabled resource ceilings before host enumeration begins.</summary>
    internal void Validate()
    {
        if (!Enum.IsDefined(CollisionPolicy) || !Enum.IsDefined(LinkPolicy) ||
            MaximumFiles <= 0 || MaximumTotalBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumFiles), "Directory import policies and resource bounds are invalid.");
        }
    }
}
