# Nintendo DS Image Tooling

This context names the concepts used to inspect, transform, and assemble Nintendo
DS-family software images without tying the model to one command-line tool.

## Language

**Image**:
A complete Nintendo DS or DSi software image, including its header, executable regions, metadata, and optional NitroFS content.
_Avoid_: ROM file, cartridge dump

**Image Document**:
The structured, in-memory representation of an Image and its addressable regions.
_Avoid_: ROM object, parsed file

**Region**:
A bounded byte range in an Image with a defined role, such as an executable, table, banner, or filesystem payload.
_Avoid_: Blob, chunk

**NitroFS**:
The hierarchical filesystem described by an Image's filename and allocation tables.
_Avoid_: Data folder, ROM files

**Program**:
An ARM7, ARM9, ARM7i, or ARM9i executable region together with its load and entry addresses.
_Avoid_: Binary, code file

**Overlay**:
A program fragment loaded on demand and described by an ARM7 or ARM9 overlay entry.
_Avoid_: Overlay file

**Banner**:
The versioned icon, localized titles, and optional DSi animation metadata shown by a system menu.
_Avoid_: Icon file

**Layout**:
The physical placement, alignment, padding, and capacity of all Regions in an Image.
_Avoid_: Offsets, packing

**Build Recipe**:
A complete declarative description from which an Image can be assembled deterministically.
_Avoid_: Project, command options

**Diagnostic**:
A structured validation finding with a stable code, severity, location, and explanation.
_Avoid_: Error string, warning text

