# Nintendo DS Image Tooling

This context names the concepts used to inspect, transform, and assemble Nintendo
DS-family software images without tying the model to one command-line tool.

## Language

**Image**:
A complete Nintendo DS or DSi software image, including its header, executable regions, metadata, and optional NitroFS content.
_Avoid_: ROM file, cartridge dump

**Carrier**:
The storage form of an Image: cartridge or digital SRL. Carrier is distinct from whether a Program executes in DS mode, DSi mode, or both.
_Avoid_: Unit code, file extension, console generation

**Digital SRL**:
An executable Image distributed as title content for DSi internal storage or an SD card, including DSiWare and system applications.
_Avoid_: DSi-exclusive cartridge, NAND dump

**Region**:
A bounded byte range in an Image with a defined role, such as an executable, table, banner, or filesystem payload.
_Avoid_: Blob, chunk

**Allocation**:
A FAT-identified Region that may be named by NitroFS, referenced by an Overlay, both, or neither.
_Avoid_: File slot, FAT file

**File ID**:
The zero-based identity of an Allocation; it is independent from an Overlay ID and may remain valid without a NitroFS name.
_Avoid_: Overlay number, file index

**NitroFS**:
The hierarchical filesystem described by an Image's filename and allocation tables.
_Avoid_: Data folder, ROM files

**Program**:
An ARM7, ARM9, ARM7i, or ARM9i executable region together with its load and entry addresses.
_Avoid_: Binary, code file

**Overlay**:
A program fragment loaded on demand and described by an ARM7 or ARM9 overlay entry.
_Avoid_: Overlay file

**Secure Area**:
The cartridge Region whose identifier, checksum, and optional KEY1 transformation are handled separately from ordinary Program bytes.
_Avoid_: Encrypted ARM9, security block

**Banner**:
The versioned icon, localized titles, and optional DSi animation metadata shown by a system menu.
_Avoid_: Icon file

**Layout**:
The physical placement, alignment, padding, and capacity of all Regions in an Image.
_Avoid_: Offsets, packing

**Physical Size**:
The number of bytes actually present in an Image's storage, including any padding or trailing material.
_Avoid_: Used size, device capacity

**Device Capacity**:
The nominal cartridge storage size declared by an Image's header, independently of its physical length. For a Digital SRL, this declaration is informational rather than a physical cartridge limit.
_Avoid_: File size, meaningful extent

**Declared Content Extent**:
The exclusive end needed to retain an Image's declared components, used-size declarations, and recognized post-used structures. Material beyond this extent is not automatically known to be disposable padding.
_Avoid_: Last nonzero byte, last non-FF byte

**Build Recipe**:
A complete declarative description from which an Image can be assembled deterministically.
_Avoid_: Project, command options

**Edit Session**:
A set of proposed changes against an existing Image that does not mutate its source and can be inspected before saving.
_Avoid_: Mutable image, patch mode

**Preservation Save**:
A save that retains unknown bytes, unchanged Regions, File IDs, and physical placement wherever the requested changes permit.
_Avoid_: Rebuild, in-place edit

**Structural Rebuild**:
A save that regenerates interdependent tables and Layout because the entry tree or component structure changed.
_Avoid_: File replacement, preservation edit

**Compatibility Profile**:
A named collection of Layout and metadata conventions used to reproduce a particular toolchain's output where those conventions differ.
_Avoid_: Compatibility mode, magic flags

**Diagnostic**:
A structured validation finding with a stable code, severity, location, and explanation.
_Avoid_: Error string, warning text

**Integrity**:
Evidence that bytes match checksums, digests, or signatures recorded by the Image; it does not by itself establish who produced them.
_Avoid_: Authenticity, validity

**Authenticity**:
A trust conclusion made only after cryptographic verification with an explicitly supplied trusted key or trust policy.
_Avoid_: Integrity, signed flag
