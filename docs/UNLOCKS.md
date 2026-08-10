# Backyard Baseball unlock and save format

The unlock state is not stored in `DATA.MET`. Ghidra analysis of the retail USA executable
`SLUS_208.65` traced it to the PS2 memory-card file named `Settings`, serialized by
`BaseballYaga::SoundOptions::SaveData`.

## Game/ISO patch (recommended)

For a modded game build, patch the executable that will be placed in the rebuilt ISO:

1. Extract the ISO and keep its directory structure.
2. Choose **Edit > Patch Game Executable Unlocks...**.
3. Select the extracted USA executable `SLUS_208.65`.
4. Select individual content or **Unlock All**, then apply.
5. Choose **File > Rebuild Game ISO...** and build from the extracted game folder.

This does not depend on a memory card. Selected content is forced unlocked for existing saves,
new saves, and no-save sessions. The editor patches the extracted executable rather than writing
the ISO container directly; the patch becomes part of the game when the ISO is rebuilt.

The patch does not change the executable's size. It modifies two Ghidra-confirmed functions:

| Function | Runtime address | File offset | Patched behavior |
| --- | ---: | ---: | --- |
| `SoundOptions::IsItemUnlocked(int)` | `0x0026B0C0` | `0x0016B140` | ORs the selected forced mask into the saved mask before testing the requested bit |
| `SoundOptions::FieldsRemainingForAquadome()` | `0x0026B0E0` | `0x0016B160` | Returns zero when Aquadome is forced unlocked |

The verified retail executable is 34,769,044 bytes with SHA-256
`DCB35FAE266F0D46DCAE7CF605830AC780CF0F199321760B3971F68350BB1FA7`. Patch safety uses the
original R5900 instruction signatures, so an unsupported revision or conflicting existing patch
is rejected. Each apply or restore creates a timestamped backup. **Restore Original Checks**
puts the verified instructions back.

## Optional save-file editing

The memory-card editor remains available for changing one save's actual progress:

1. Back up the full memory card.
2. Export the Backyard Baseball file named `Settings` with a PS2 memory-card tool.
3. Choose **Edit > Edit Exported Save Unlocks (Optional)...**.
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
