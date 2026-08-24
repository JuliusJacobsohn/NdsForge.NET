# Nitro graphics {#nitro_graphics}

`NdsForge.Graphics` is the optional, dependency-light feature package for native Nintendo DS indexed graphics. It references `NdsForge.Nitro` for shared platform types but does not bring in PNG, JPEG, GIF, or UI frameworks. A future `NdsForge.Graphics.ImageSharp` adapter can provide host-image import/export and quantization without imposing those dependencies on ROM, archive, or native-resource consumers.

## Colors and NCLR palettes

`NitroColor555` preserves the complete stored BGR555 word, including bit 15, and converts to or from dependency-free `RgbaColor32` values. Alpha is not embedded in an NCLR color; transparent palette-index policy belongs to the tile/map/sprite composition layer.

```csharp
using NdsForge.Graphics.Colors;
using NdsForge.Graphics.Palettes;

NclrPalette palette = NclrPalette.Parse(File.ReadAllBytes("icon.NCLR"));
RgbaColor32 preview = palette.Colors[1].ToRgba32();

byte[] edited = palette.CreateBuilder()
    .ReplaceColor(1, NitroColor555.FromRgba32(new(255, 96, 32)))
    .Build();
File.WriteAllBytes("icon-edited.NCLR", edited);
```

NCLR parsing validates the standard header, PLTT bounds and color depth, and optional PCMP target-palette map. The declared color byte count is exposed because some producers leave it inconsistent; the bounded PLTT section and data offset determine the actual stored words. Preservation builds retain unknown blocks, padding, and allocation trailing bytes, while canonical builds emit deterministic PLTT/PCMP structures.

The private compatibility suite covers all 4,006 direct NCLR files and 1,042,096 colors in the ROM corpus. Every file must preserve exactly and canonically rebuild/reparse. Color expansion, depth, extended-palette state, target mapping, palette partitioning, and every interpreted color were compared against the separately compiled current Texim implementation.
