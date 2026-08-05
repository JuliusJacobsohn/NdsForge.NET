# NdsForge.NET

NdsForge.NET is an independent, pure C# toolkit for inspecting, extracting, editing,
validating, and building Nintendo DS and DSi software images. It targets .NET 10
and is designed first as an object-oriented library, with a thin CLI for people
and build pipelines.

The project is in active development. Its compatibility target is the practical
feature set of `ndstool`, expanded with stream-first APIs, selective and lossless
editing, structured diagnostics, deterministic builds, cancellation, and safe
path handling.

NdsForge.NET is an independent project. It is not affiliated with Nintendo or
devkitPro and contains no Nintendo ROMs, keys, firmware, or copyrighted assets.

## Development

Requirements: the .NET 10 SDK.

```powershell
dotnet restore
dotnet build --no-restore
dotnet test --no-build
dotnet pack --no-build --output artifacts/packages
```

Private `.nds`, `.srl`, `.dsi`, save, and extracted binary fixtures are ignored.
Tests that use a legally obtained local image are opt-in and never copy it into
the repository or test output.

## License

NdsForge.NET is licensed under the MIT License. The implementation is written
from public format documentation and independently designed APIs, with existing
tools used to identify expected behavior. GPL-licensed code is not copied,
translated, linked, or included in this repository. This engineering boundary is
not a legal opinion; licensing should be reviewed before a public release.
