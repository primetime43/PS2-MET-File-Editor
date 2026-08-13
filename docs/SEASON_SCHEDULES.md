# Season schedule templates

The **Season Schedule Editor** modifies the matchup templates under `data/schedules/` in
`DATA.MET`. The retail USA archive contains 40 templates:

- `templateschedule18_00.dat` through `templateschedule18_19.dat`
- `templateschedule32_00.dat` through `templateschedule32_19.dat`

The game selects one of these templates when a new season is created. Editing them does not rewrite
the completed games already stored in an existing memory-card season.

## Editor behavior

Choose the 18-game or 32-game family, one of its 20 template variants, and a round. Each round shows
12 ordered matchups covering the game's 24 active season-team slots.

The IDs are intentionally displayed as `Team slot 01 (ID 0)` through `Team slot 24 (ID 23)`.
They are positions in the newly built season league, not permanent MLB franchise IDs. The actual
club assigned to a slot depends on the season setup.

Every valid round must contain every slot exactly once. If a slot selected in a dropdown already
appears elsewhere in the round, the editor swaps it with the old value automatically. This keeps
the template valid while allowing any matchup to be rearranged. **Swap Team A / B** reverses the
two stored participants without changing the rest of the round.

The overview reports, for each slot:

- total games;
- appearances on the Team A and Team B sides;
- unique opponents;
- repeated matchups.

The binary templates store two ordered participants but no separate home/away flag, so the editor
uses the accurate labels **Team A** and **Team B**. Scores and season results are separate save data.

## Recovered binary layout

All schedule files are exactly `0xC00` (3,072) bytes and use little-endian signed 32-bit integers.

| Family | Used values | Interpretation | Remaining values |
| --- | ---: | --- | --- |
| 18-game | 432 | 18 rounds × 12 games × 2 team IDs | 336 copies of `0xCCCCCCCC` padding |
| 32-game | 768 | 32 rounds × 12 games × 2 team IDs | none |

Within a round, consecutive values form one matchup. Each round is a permutation of IDs 0 through
23. The editor validates this invariant before writing, keeps every entry at exactly 3,072 bytes,
and preserves the 18-game family's padding byte-for-byte.

## Executable confirmation

The USA executable's symbol table and Ghidra decompilation confirm the in-memory representation:

| Routine | Address | Recovered behavior |
| --- | ---: | --- |
| `SeasonSchedule::LoadData` | `0x00264A80` | Loads 32 days of 12 four-integer game-result records |
| `SeasonSchedule::GetTeamRecord` | `0x00264FC0` | Reads the two team IDs and their two saved scores |
| `SeasonSchedule::GetDayGameData` | `0x002650F0` / `0x00265110` | Returns a selected day's game record |
| `SeasonSchedule::CreateTeamSchedule` | `0x00265130` | Builds the active season schedule from the template |
| `SeasonSchedule::Reset` | `0x00265220` | Resets the in-memory schedule state |

The template contains only the first two fields needed to construct each matchup. The runtime and
memory-card season state add the two result/score fields, which is why changing `DATA.MET` affects
new seasons rather than completed games in an existing one.

## Saving and recovery

**Save Schedules to DATA.MET** validates every changed template and creates one timestamped backup
before replacing any entries. Because replacements are fixed-size, no archive offsets need to move.
If any write fails, the batch editor restores the backup automatically.
