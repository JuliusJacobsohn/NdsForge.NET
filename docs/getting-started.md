# Getting started {#getting_started}

NdsForge separates reading, validation, preservation edits, and structural builds so an application can choose the least destructive workflow that fits its task.

## Open and navigate an image

`NdsImage.Open` and `OpenAsync` accept a path. Stream overloads retain the caller's stream ownership, while memory overloads are useful for small synthetic images and tests. Keep the returned image alive while opening payload streams backed by its source.

```csharp
await using NdsImage image = await NdsImage.OpenAsync("game.nds");

foreach (NdsDirectory directory in image.FileSystem.Root.Traverse())
{
    Console.WriteLine(directory.FullPath);
}

NdsFile file = image.FileSystem.GetFile("/data/config.bin");
await using Stream content = file.Data.OpenRead();
```

Use `NdsReadOptions` to lower allocation, table, overlay, or hierarchy limits when processing uploads or other untrusted input.

## Validate before acting

Parsing answers “can this structure be represented safely?” Validation answers “is the represented image internally consistent under these trust inputs?”

```csharp
NdsValidationResult validation = image.Validate(new NdsValidationOptions
{
    VerifyFileOverlaps = true,
});

if (!validation.IsValid)
{
    foreach (NdsDiagnostic error in validation.Diagnostics)
    {
        Console.Error.WriteLine($"{error.Code}: {error.Message}");
    }
}
```

Diagnostics have stable codes and severities suitable for application logic. Messages are explanations for humans and should not be parsed.

## Extract selected content

```csharp
await image.ExtractAsync(
    "workspace",
    new NdsExtractionOptions
    {
        Components = NdsImageComponent.Programs |
            NdsImageComponent.NitroFileSystem |
            NdsImageComponent.Banner,
        OverwritePolicy = NdsOverwritePolicy.Fail,
        FileFilter = file => file.FullPath.StartsWith("/data/", StringComparison.Ordinal),
    });
```

Extraction validates every host path and refuses traversal, reserved names, collisions, and reparse-point redirection. Choose an overwrite policy explicitly for repeatable automation.

## Choose editing or rebuilding

Use `NdsImageEditor` when preserving the original layout matters. It can change supported header values, replace allocated files, and repair checksums. Review `NdsImageEditor.Plan` before saving when an application needs to explain physical changes.

Use `NdsImageBuilder` for new images or structural changes such as adding and removing NitroFS paths. Builders own copies of supplied byte buffers and produce deterministic output from identical state.

```csharp
NdsImageBuilder builder = await NdsImageBuilder.FromImageAsync(image);
if (builder.DsMetadata is not null)
{
    // Explicitly retain stored late-DS fields, accepting an unverified/stale warning.
    builder.DsMetadata.Integrity = NdsDsIntegrityOptions.PreserveStored;
}
await builder.FileSystem.ImportDirectoryAsync("replacement-data");
NdsImageBuildResult result = await builder.WriteAsync("rebuilt.nds");
foreach (NdsDiagnostic diagnostic in result.Diagnostics)
{
    Console.WriteLine($"{diagnostic.Code}: {diagnostic.Message}");
}
```

The default profile uses NdsForge's deterministic layout. Select `NdsImageBuildProfile.Ndstool1503` only when compatibility with the historical tool's verified layout is an explicit requirement.

For an authenticated late-DS rebuild, select `NdsDsIntegrityOptions.CreateHmacSha1` with your separate program and banner keys, KEY1 table, and optional signing provider/public-key pair. The builder coordinates classic overlay records, ARM9 recompression, late-DS digests, and the header signature. `NdsDsIntegrityOptions.Unauthenticated` explicitly removes only late-DS authentication declarations and fields. For preservation edits, pass the same policy through `new NdsWriteOptions { DsIntegrity = policy }`. Read [formats and safety](formats-and-safety.md) for required layouts, missing-key behavior, and the distinction between HMAC generation and signature trust.

## Next steps

- Read [core concepts](concepts.md) for ownership, mutability, and workflow boundaries.
- Read [formats and safety](formats-and-safety.md) before processing unknown images or key material.
- Read the [CLI reference](cli.md) for automation without writing an application.
- Browse the generated API reference for exact members, constraints, exceptions, and examples.
