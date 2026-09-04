# Nitro graphics {#nitro_graphics}

`NdsForge.Graphics` is the optional, dependency-light feature package for native Nintendo DS indexed graphics. It references `NdsForge.Nitro` for shared platform types. Native pixels, palettes, tiling, rendering, and quantization belong in this package. PNG, JPEG, GIF, and other host-image codecs belong in a separate adapter, such as a future `NdsForge.Graphics.ImageSharp` package. No host-codec adapter is currently required or shipped. Package validation and runtime-dependency tests enforce this boundary.

## RGBA conversion and Banners

`IndexedImage4.FromRgba32` accepts straight-alpha row-major pixels and returns
four-bit indices plus sixteen BGR555 palette words. Index zero is reserved for
transparency by default, including for fully opaque input. A Banner therefore
has fifteen opaque color slots. The result feeds the core Banner builder without
adding a dependency between the core and graphics packages:

```csharp
using NdsForge;
using NdsForge.Graphics.Colors;
using NdsForge.Graphics.Images;

RgbaColor32[] pixels = Enumerable.Repeat(new RgbaColor32(255, 96, 32), 1024).ToArray();
IndexedImage4 icon = IndexedImage4.FromRgba32(32, 32, pixels);
NdsBanner banner = new NdsBannerBuilder()
    .SetTitle(NdsBannerLanguage.English, "Example")
    .SetIndexedIcon(icon.PaletteIndices.Span, icon.Palette.Span)
    .Build();
```

The conversion contract is explicit:

- Alpha at or below `AlphaThreshold` becomes transparent; its RGB is ignored.
  The default threshold is zero. Higher alpha becomes opaque without blending.
  With `ReserveTransparentIndex = false`, alpha is ignored and all sixteen entries
  can be opaque; that setting is unsuitable for Banners.
- `ColorReduction` defaults to `DiscardLowBits`, which removes the low three bits
  per RGB channel. `Nearest` uses the existing `NitroColor555.FromRgba32` rounding
  rule. Packing happens before duplicate-color detection. Partial-alpha values
  do not create duplicate opaque palette entries.
- Colors that fit retain first row-major occurrence order. Unused palette entries
  are zero. `ColorCount` includes the reserved transparent slot.
- Excess colors use frequency-weighted seed selection followed by at most eight
  integer nearest-center refinement rounds in five-bit RGB space. Seed-score ties
  use the smaller packed color; distance ties use the earlier palette entry. There
  is no dithering or random state. This is a deterministic policy, not a claim of
  globally optimal or perceptually uniform color fitting.
- `PaletteOverflow = IndexedPaletteOverflow.Reject` requires exact packed opaque
  colors. `WasColorReduced` reports changes to packed colors, excluding initial
  RGB packing and alpha thresholding.
- Dimensions must be positive and match the pixel count. `MaximumPixels` defaults
  to 16,777,216 and is checked before output allocation. Empty images are rejected;
  a nonempty fully transparent image is supported. This is an input-pixel bound,
  not a process-memory budget.

`MapToPalette` uses a supplied ordered palette. It preserves duplicates, unused
entries, and high bits; bit 15 does not affect RGB distance. The reserved index
never matches an opaque pixel. Reject mode requires an exact five-bit match.
Equivalent palette colors choose the first eligible entry, so an RGBA round trip
cannot recover distinct source indices that originally had identical colors.

`EncodeTiles` emits complete 8-by-8 tiles, with the left pixel in the low nibble;
both dimensions must be multiples of eight. `EncodePalette` returns 32 little-endian
bytes. `Render` uses `NitroColor555` full-range expansion and transparent black for
index zero. Other renderers can expand five-bit channels differently by one
eight-bit step; comparisons therefore use packed colors and transparency.

For DSi animation, convert each 32-by-32 source frame and use `SetAnimatedFrame`:

```csharp
var animated = new NdsBannerBuilder(0x0103)
    .SetIndexedIcon(icon.PaletteIndices.Span, icon.Palette.Span);
for (int slot = 0; slot < 8; slot++)
{
    RgbaColor32[] framePixels = Enumerable.Repeat(
        new RgbaColor32((byte)(slot * 32), 96, 192), 1024).ToArray();
    IndexedImage4 frame = IndexedImage4.FromRgba32(32, 32, framePixels);
    animated.SetAnimatedFrame(slot, frame.PaletteIndices.Span, frame.Palette.Span);
}
animated.SetAnimationSequence(Enumerable.Range(0, 8)
    .Select(slot => new NdsBannerAnimationStep(10, (byte)slot, (byte)slot)));
NdsBanner animation = animated.Build();
```

Tile and palette selectors remain independent. The example pairs each frame with
its own palette. For palette cycling, establish consistent index meaning across
palettes; independently reducing each frame does not establish that relationship.
The core builder validates frame metadata and computes Banner CRCs.

Tests lock complete bytes for sixteen static-icon examples across all four Banner
versions, plus an eight-frame animated example with a 63-step sequence. The private
corpus check covers 142 cartridge and five Digital SRL static icons, all 640
tile/palette combinations in ten animated Banners, and 226 sequence entries.
Visible colors and transparency must
survive conversion and rebuilding; source palette-index aliases need not survive.

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
