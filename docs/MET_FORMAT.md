# Backyard Baseball 2004 MET format

This document combines validation of the retail USA `DATA.MET` with the matching loader code in `SLUS_208.65`. Addresses are runtime addresses from an R5900-aware Ghidra import.

## Archive layout

All integer fields are unsigned 32-bit little-endian values.

| Offset | Size | Meaning |
| --- | ---: | --- |
| `0x00` | 4 | Absolute data-section offset; the loader derives the directory size as this value minus 8 |
| `0x04` | 4 | Data-section size (`file size - data-section offset`) |
| `0x08` | variable | Packed file-entry records followed by zero padding |
| header word 0 | variable | File payloads, each starting on a 2,048-byte boundary |

The USA archive has these measured properties:

- File size: 850,393,092 bytes
- Data-section offset: 1,308,672 (`0x0013F800`)
- Data-section size: 849,084,420 (`0x329C0004`)
- Entries: 24,759
- Directory padding: 171 zero bytes
- Every payload offset is aligned to a 2,048-byte disc sector
- 24,628 of 24,758 adjacent entry pairs have a nonzero alignment gap

## File-entry record

| Relative offset | Size | Meaning |
| --- | ---: | --- |
| `+0x00` | 4 | Absolute payload offset in `DATA.MET` |
| `+0x04` | 4 | Logical payload size |
| `+0x08` | 4 | Path byte length |
| `+0x0C` | path length | ASCII path without a terminator |

Although the path-length field occupies four bytes, the retail loader reads only its low byte. Valid paths must therefore be at most 255 bytes.

The directory does not require a unique terminator record. The loader reads exactly `dataSectionOffset - 8` bytes and treats zero padding as empty records because their size word is zero.

## Ghidra findings

The executable must be imported with the Emotion Engine/R5900 language. Generic MIPS64 cannot decode PS2-specific instructions such as `SQ` and truncates these functions.

| Address | Symbol | Recovered behavior |
| --- | --- | --- |
| `0x0035DBB0` | `yagares::CMetaFile::Init(char const*)` | Opens and caches the MET stream, reads the 8-byte global header, reads `header[0] - 8` directory bytes, and walks packed records. Records with a nonzero size are passed to `AddFile`. |
| `0x0035C300` | `yagares::CMetaFile::AddFile(...)` | Splits paths on `/` or `\\`, builds the directory tree, and stores the record's `(offset, size)` pair on leaf entries. |
| `0x0035EC60` | `yagares::CMetaFileStream::Init(...)` | Copies the leaf's offset and size into stream fields at `+0x0C` and `+0x10`. |
| `0x0035EA00` | `yagares::CMetaFileStream::ReadDirect(...)` | Clamps reads to the stored logical size and seeks the backing stream to `base offset + current position`. |
| `0x0035EB60` | `yagares::CMetaFileStream::Read(...)` | Uses the same base-offset seek behavior for byte-sequence reads. |

The decompiled `CMetaFile::Init` visibly consumes header word 0 to size the directory read. Header word 1 is not used in this routine, but it exactly equals the data-section byte count in the retail archive and must be kept consistent when rebuilding.

## Safe resize rules

When a payload grows:

1. Update its logical size in the entry record.
2. Consume its existing sector-padding gap first.
3. If it no longer fits, move the following tail by a whole number of 2,048-byte sectors.
4. Add that sector shift to every later entry offset.
5. Update global header word 1 to the new data-section size.
6. Preserve directory bytes and the untouched data tail byte-for-byte; regenerate only the resized entry's zero padding.

Shifting later offsets by only the raw payload-size delta breaks sector alignment. Copying the original 8-byte global header without updating word 1 leaves stale archive metadata. Both behaviors existed in the old editor rebuild path and are covered by regression tests now.


## Unlock logic is outside DATA.MET

The retail executable's unlock checks do not read `DATA.MET`. Normal progress is stored in the
memory-card file named `Settings`, but a modded ISO can force selected content unlocked by
patching the extracted `SLUS_208.65` executable before rebuilding. See
[Backyard Baseball unlock and save format](UNLOCKS.md) for the executable patch points,
recovered mask, and optional save-file layout.
