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

NCLR, NCER, and NANR accept up to three zero bytes after the final block when
these bytes align the declared file length to four bytes. Nonzero or excessive
declared padding is rejected; allocation bytes beyond the declared length
remain preserved separately.

The private compatibility suite covers 9,119 supported direct NCLR files and 3,391,003 colors in the ROM corpus. Every supported file must preserve exactly and canonically rebuild/reparse. Color expansion, depth, extended-palette state, target mapping, palette partitioning, and every interpreted color are locked to a reviewed compatibility baseline. An additional 26 files declare non-indexed format values outside this palette API; their exact allocation hashes and explicit rejection are tracked separately.

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

The private suite covers 10,797 supported NCGR files containing 215,348,672 indexed pixels and 4,526 NSCR files containing 5,368,694 map entries. Every interpreted pixel, dimension, depth, storage order, mapping boundary, background mode, palette mode, tile number, flip, and palette selector is locked to a reviewed compatibility baseline. Thirteen unrelated allocations beginning with the bytes `RGCN` are intentionally rejected because they do not contain a valid standard-file byte-order marker. Another 26 graphics allocations declare formats outside the 4/8-bit indexed API; their hashes and explicit rejection are tracked without claiming pixel conversion support.

## NCER sprite cells and OAM

`NcerCellBank` reads bounded CEBK cell tables, optional boundaries and UACT values, all three exact OAM words, object-character mapping metadata, and opaque LABL/UEXT payloads. `NitroObjectEntry` projects signed coordinates, every legal hardware size/shape, depth, priority, palette, flip, affine group, mosaic, disabled/double-size state, and rendering mode without discarding reserved bits. Builders can patch one OAM object byte-exactly or emit a deterministic NCER while retaining ambiguous label data opaquely.

Unknown object-character mapping values remain available through the enum's
raw integer value and survive both preserved and canonical builds. Preserving
an unknown value does not imply that a renderer can interpret that mapping.

The corpus test covers 6,484 NCER files, 98,486 cells, and 591,129 objects. All typed OAM fields are locked to a reviewed compatibility baseline, while exact raw-word digests, no-op preservation, opaque auxiliary blocks, cell bounds, and canonical reparse are independently locked by the NdsForge suite.

## NANR cell animations

`NanrAnimationBank` provides a bounded read-only view of ABNK animation sequences. Each sequence retains its payload variant, playback and loop words, flags, descriptor offset, and ordered frames. Each `NanrFrame` exposes duration, descriptor flags, payload offset, and the referenced NCER cell index. Ambiguous LABL and UEXT payloads remain opaque, and `WritePreserved` returns an isolated byte-exact copy of the original allocation.

```csharp
using NdsForge.Graphics.Animations;

NanrAnimationBank animation = NanrAnimationBank.Parse(File.ReadAllBytes("sprite.NANR"));
foreach (NanrSequence sequence in animation.Sequences)
{
    foreach (NanrFrame frame in sequence.Frames)
        Console.WriteLine($"cell {frame.CellIndex}, duration {frame.Duration}");
}
```

The private compatibility suite covers all 5,719 direct NANR files, 37,364 sequences, and 161,873 frame references in the ROM corpus. It locks all three observed payload variants, every exposed sequence and descriptor word, every cell reference, opaque auxiliary sections, and byte-exact preservation to a reviewed semantic digest.

## NFTR bitmap fonts

`NftrFont` provides a bounded model of FINF metadata, indexed CGLP glyph cells,
per-glyph CWDH placement and advance metrics, and linked CMAP character maps.
Direct, table, and sparse map methods are normalized to character/glyph pairs;
unmapped table entries remain absent from lookup results. Glyph pixels are
exposed as row-major indices in their stored orientation, while the original
rotation flags remain available for consumers that need to apply a display
transform.

```csharp
using NdsForge.Graphics.Fonts;

NftrFont font = NftrFont.Parse(File.ReadAllBytes("dialog.nftr"));
if (font.TryGetGlyphIndex(0x41, out ushort glyphIndex))
{
    NftrGlyph glyph = font.Glyphs[glyphIndex];
    Console.WriteLine($"{glyph.Metrics.BearingX}, {glyph.Metrics.AdvanceWidth}");
}

byte[] pixels = font.Glyphs[0].StoredPixels.ToArray();
pixels[0] = (byte)((1 << font.BitsPerPixel) - 1);
byte[] edited = font.CreateBuilder().ReplaceGlyphPixels(0, pixels).Build();
```

Preservation builds patch only explicitly changed glyph bytes and metric
records, retaining linked-block layout, unknown bytes, padding, and trailing
allocation data. Canonical builds deterministically reconstruct FINF, CGLP,
CWDH, and CMAP blocks and are intended for normalized output. The compatibility
suite covers all 105 direct NFTR files in the private corpus: 63,351 glyphs,
69,348 character-map slots (63,351 mapped characters), all four observed text
encodings, all three CMAP methods, and one-, two-, three-, and four-bit indexed cells.
The semantic baseline includes every exposed metadata field, glyph metric,
pixel index, and resolved mapping, and canonical outputs must reparse to the
same model.
