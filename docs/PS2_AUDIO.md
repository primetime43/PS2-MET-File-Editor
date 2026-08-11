# PS2 audio parsing and export

The DATA.MET browser understands the two PlayStation ADPCM layouts used by Backyard Baseball:

- `.vag` is a standalone `VAGp` clip with a 48-byte big-endian header.
- `.mih` and `.mib` are a Sony MultiStream pair. The 64-byte little-endian MIH header describes the
  channel count, sample rate, interleave layout, and final partial block; the MIB contains the
  interleaved PSX ADPCM frames.

Selecting any of these entries shows the format, channel count, sample rate, decoded sample count,
duration, and compressed size. MIH/MIB entries additionally show both archive paths, the per-channel
interleave size, block count, and usable bytes in the final block. Selecting either half of a pair
finds the companion entry automatically.

## Export options

**Export Selected** offers these choices:

| Selected entry | Export | Result |
| --- | --- | --- |
| `.vag` | Decoded PCM WAV | Mono 16-bit PCM WAV at the VAG's original sample rate |
| `.vag` | Original VAG | Byte-for-byte archive payload |
| `.mih` or `.mib` | Decoded PCM WAV | Interleaved multichannel 16-bit PCM WAV at the MIH sample rate |
| `.mih` or `.mib` | Original MIH/MIB pair | Two byte-for-byte files with the same selected base name |

WAV conversion is built into the editor and does not need FFmpeg or another decoder. Raw pair export
is the lossless choice for editing with another PS2 audio tool or re-importing later.

## MIH fields used by the editor

| Offset | Size | Meaning |
| --- | --- | --- |
| `0x00` | 4 | Header size (`0x40`) |
| `0x04` | 4 | Final usable interleave bytes in the upper 24 bits; low byte is padding metadata |
| `0x08` | 4 | Channel count |
| `0x0C` | 4 | Sample rate |
| `0x10` | 4 | Interleave block size per channel |
| `0x14` | 4 | Interleave block count |

The decoder validates channel count, sample rate, 16-byte ADPCM alignment, companion presence, and
the physical MIB size before allocating or decoding the output. The implementation was checked
against all 93 matching MIH/MIB pairs in the retail USA `DATA.MET`.
