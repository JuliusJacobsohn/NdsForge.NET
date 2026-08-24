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

## NCGR tiles and NSCR maps

`NcgrCharacterGraphics` exposes row-major color indices while retaining the native NCGR depth, tile-mapping boundary, tiled-versus-linear storage flag, optional CPOS source rectangle, and unspecified-dimension convention. This makes pixel operations independent of how the CHAR payload is serialized. Preservation builds patch the original payload without disturbing unknown blocks or trailing allocation bytes; canonical builds deterministically restore the requested tile order and 4-bit nibble packing.

`NscrScreenMap` provides typed text, affine, and extended entries. Each `NscrMapEntry` exposes its tile number, horizontal/vertical flips, and palette selector while retaining the exact standard 16-bit representation. The dependency-free renderer composes an NSCR, NCGR, and NCLR into `RgbaImage32`; a host adapter can then encode that raster without making PNG or UI libraries transitively mandatory.

```csharp
using NdsForge.Graphics.Maps;
using NdsForge.Graphics.Images;
using NdsForge.Graphics.Tiles;

NcgrCharacterGraphics tiles = NcgrCharacterGraphics.Parse(File.ReadAllBytes("background.NCGR"));
NscrScreenMap map = NscrScreenMap.Parse(File.ReadAllBytes("background.NSCR"));
RgbaImage32 raster = map.Render(tiles, palette);

byte[] editedMap = map.CreateBuilder()
    .ReplaceEntry(0, 0, new NscrMapEntry(tileIndex: 4, horizontalFlip: true))
    .Build();
```

Differential verification covers 5,035 valid NCGR files containing 126,167,104 indexed pixels and 1,231 NSCR files containing 1,274,624 map entries. Every interpreted pixel, dimension, depth, storage order, mapping boundary, background mode, palette mode, tile number, flip, and palette selector matched the separately compiled current Texim implementation. Thirteen unrelated allocations beginning with the bytes `RGCN` are intentionally rejected because they do not contain a valid standard-file byte-order marker.
