# Backyard Baseball unlock/save format

The unlock state is not stored in `DATA.MET`. Ghidra analysis of the retail USA executable
`SLUS_208.65` traced it to the PS2 memory-card file named `Settings`, serialized by
`BaseballYaga::SoundOptions::SaveData`.

## Safe workflow

1. Back up the full memory card.
2. Export the Backyard Baseball file named `Settings` with a PS2 memory-card tool.
3. In the editor, choose **Edit > Unlock Content (Settings Save)...**.
4. Select the exported `Settings` file, choose content, and save.
5. Import the edited file back into the same game save.

The editor validates the file before showing any controls. Saving creates a timestamped sibling
backup, preserves unknown data, updates the unlock mask, and recalculates the trailing CRC-32.

## File layout

All multi-byte values are little-endian.

| Offset | Size | Meaning |
| --- | ---: | --- |
| `0x00` | 4 | Serialized data-block length |
| `0x04` | 4 | Save-data version |
| `0x08` | variable | Sound/options fields |
| `0x24` | 4 | Unlock bit mask |
| `0x28` | variable | Hall of Fame data |
| data-block length | 4 | CRC-32 of the data block |

The physical file length must equal the value at `0x00` plus four CRC bytes. The checksum is
standard reflected CRC-32: polynomial `0xEDB88320`, initial value `0xFFFFFFFF`, and final
complement. This is the same algorithm represented by check value `CBF43926` for ASCII
`123456789`.

## Unlock mask

The executable's `IsItemUnlocked(index)` checks `mask & (1 << index)`; `UnlockItem(index)`
sets that bit.

| Bit | Mask | Content |
| ---: | ---: | --- |
| 0 | `0x0001` | Abner Dubbleplay |
| 1 | `0x0002` | Mr. Clanky |
| 2 | `0x0004` | Barry DeJay |
| 3 | `0x0008` | Randy Johnson |
| 4 | `0x0010` | Pedro Martinez |
| 5 | `0x0020` | Mike Piazza |
| 6 | `0x0040` | Derek Jeter |
| 7 | `0x0080` | Greg Maddux |
| 8 | `0x0100` | Shawn Green |
| 9 | `0x0200` | Humongous Entertainment Stadium |
| 10 | `0x0400` | Quantum Field |
| 11 | `0x0800` | Darts minigame |
| 12-15 | `0xF000` | Aquadome progress flags |

The game's Aquadome check reaches unlocked after any three of bits 12-15 are set. The editor
uses all four for a deterministic fully-unlocked state. If an existing save has only some of
those bits, the checkbox is shown as indeterminate and those bits are preserved unless changed.

Unknown mask bits and all unrelated Settings data are preserved.

## DATA.MET mods

Game tuning values are ordinary archive entries rather than unlock persistence. Known useful
editable files include:

- `data/options/debugoptions.ini`: AI swing, catch/miss, pitching error, and timer switches.
- `data/options/fields.ini`: friction, collision, bounce, and roll values for fields.
- `data/options/menuoptions.ini`: rules, display defaults, contestants, and sound options.
- field and player text/config files for stats and metadata.

Text files can be edited normally. Any entry can be changed in **View > Hex Editor** using
whitespace-separated byte pairs. Hex mode covers payload bytes only; it never exposes archive
header bytes. Import File also writes the selected file byte-for-byte, including trailing zeros.
