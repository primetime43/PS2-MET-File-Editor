# Backyard Baseball PS2 ANM animations

`DATA.MET` contains 2,884 `.anm` files. They are RenderWare animations, not a game-specific text
format. The editor parses every retail file, reconstructs its implicit tracks, and overlays a matching
EVT eye/mouth timeline when one can be identified safely.

## Retail inventory

The USA `DATA.MET` used for validation contains:

| Item | Count |
| --- | ---: |
| ANM files | 2,884 |
| Standard scheme 1 | 2,502 |
| Compressed scheme 2 | 382 |
| Decoded keyframes | 683,783 |
| Reconstructed tracks | 64,926 |
| ANM files with an unambiguous paired EVT | 1,026 |
| Largest animation | 5,132 keyframes |
| Most tracks in one animation | 79 |

All 2,884 retail entries were forced through header, frame, transform, previous-link, and track parsing.

## Common 32-byte header

All values are little-endian. Offsets are from the beginning of the ANM entry.

| Offset | Type | Meaning |
| ---: | --- | --- |
| `0x00` | `uint32` | RenderWare chunk ID, `0x1B` |
| `0x04` | `uint32` | Payload length, file size minus 12 |
| `0x08` | `uint32` | RenderWare library/version value |
| `0x0C` | `uint32` | Animation version, `0x100` |
| `0x10` | `uint32` | Interpolation scheme: 1 or 2 |
| `0x14` | `uint32` | Keyframe count |
| `0x18` | `uint32` | Flags; zero in the retail files |
| `0x1C` | `float32` | Animation duration in seconds |

## Scheme 1: standard keyframes

Standard files have an exact size of `32 + keyframeCount * 36`. Each 36-byte keyframe is:

| Frame offset | Type | Meaning |
| ---: | --- | --- |
| `0x00` | `float32` | Time in seconds |
| `0x04` | 4 x `float32` | Quaternion X, Y, Z, W |
| `0x14` | 3 x `float32` | Translation X, Y, Z |
| `0x20` | `int32` | Previous keyframe's byte offset in the decoded frame array |

The decoded in-memory frame size is also 36 bytes, so a non-root previous-frame value divided by 36
is its previous keyframe index.

## Scheme 2: compressed keyframes

Compressed files have an exact size of `32 + keyframeCount * 22 + 24`. Each 22-byte disk keyframe is:

| Frame offset | Type | Meaning |
| ---: | --- | --- |
| `0x00` | `float32` | Time in seconds |
| `0x04` | 4 x custom `uint16` | Compressed quaternion X, Y, Z, W |
| `0x0C` | 3 x custom `uint16` | Normalized translation X, Y, Z |
| `0x12` | `int32` | Previous keyframe's byte offset in the decoded frame array |

The final 24 bytes are six floats: translation offset X/Y/Z followed by translation scale X/Y/Z.
Translation is decoded as `compressedValue * scale + offset`. The decoded in-memory keyframe is 24
bytes, so previous-frame links divide by 24 rather than the 22-byte disk record size.

The custom 16-bit float uses bit 15 for sign, bits 11-14 for exponent, and bits 0-10 for mantissa.
The game's loader expands those fields into a normal IEEE-754 float with a `0x38000000` exponent bias.

## How tracks are recovered

ANM does not store a track table or bone names. Root keyframes are stored first. The first later frame
whose previous-frame offset is zero marks the end of that root block; that index is the track count.
Every later frame inherits the track number of its validated earlier previous-frame link.

This matches RenderWare's `RtAnimAnimationGetNumNodes` behavior. The editor deliberately displays
`Track 0`, `Track 1`, and so on because assigning invented bone names would be misleading. Connecting
these indices to named model skeleton nodes requires additional DFF/HAnim hierarchy work.

## EVT synchronization

An EVT is paired only when either:

1. it has the same full path and filename stem as the ANM, or
2. it is in the same directory and has the same canonical stem after removing that directory's
   character prefix and the batting `bat`/`bat_` marker.

The fallback is accepted only when exactly one EVT matches. This conservative rule produces 1,026
unambiguous pairs in the retail archive. EVT assignments are an overlay: changing ANM duration does not
silently rescale or rewrite its EVT file.

## Safe editing

The editor currently writes only normal `float32` timing fields:

- header duration;
- every keyframe time when duration/speed is scaled;
- a selected keyframe time, constrained between its linked track neighbors.

Quaternion data, translations, compression data, and previous-frame links remain byte-for-byte intact.
Because timing fields are replaced in place, an edited ANM remains exactly the same size. Saving creates
a timestamped `DATA.MET` backup first.

Animation assignment editing is intentionally not guessed from filenames. Assignments live in callers
and game/model data rather than this ANM container; that is separate reverse-engineering work.

## Executable confirmation

The layouts were cross-checked in the retail USA executable in Ghidra:

| Function | Address | Relevant behavior |
| --- | ---: | --- |
| `RpHAnimKeyFrameStreamRead` | `0x003DC0B0` | Reads standard 36-byte frames and rebases previous links by `0x24` |
| `RtCompressedKeyFrameStreamRead` | `0x004623B0` | Expands scheme-2 frames and translation custom data |
| `RtCompressedKeyFrameStreamWrite` | `0x004624D0` | Writes scheme-2 records |
| `RtCompressedKeyFrameStreamGetSize` | `0x004625D0` | Computes compressed stream size |
| `RtAnimAnimationGetNumNodes` | `0x0045E160` | Counts root frames/tracks using the previous-frame links |

Addresses apply to the USA `SLUS_208.65` executable.
