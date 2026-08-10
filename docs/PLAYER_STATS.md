# Backyard Baseball player stats (`*_stats.dat`)

The player editor targets the records in `DATA.MET` under `data/kids/stats/`. These are game data,
not memory-card progress. The format was confirmed against the retail USA archive and the
`SLUS_208.65` loader in Ghidra.

## Record layout

All integers are signed 16-bit little-endian values. A normal record contains 31 integers (62
bytes). A clone record contains the same 31 integers followed by eight clone-appearance integers
(78 bytes total). The numeric prefix is followed immediately by ASCII:

```text
first name,nickname,last name,
```

The game loader at `0x001512E0` reads exactly `0x3e` bytes for the common prefix. For clone IDs it
then reads another `0x10` bytes into the clone appearance area before reading the three
comma-delimited names.

| File index | Object offset | Meaning |
| ---: | ---: | --- |
| 0 | `+0x08` | Power component A |
| 1 | `+0x0a` | Batting power; headline Power rating and bat speed |
| 2 | `+0x0c` | Fielding component A |
| 3 | `+0x0e` | Coordination / contact component A |
| 4 | `+0x10` | Contact component B |
| 5 | `+0x12` | Throw speed / pitching base |
| 6 | `+0x14` | Fielding component B |
| 7 | `+0x16` | Run speed; headline Running rating |
| 8 | `+0x18` | Contact component C |
| 9 | `+0x1a` | Reaction / fielding component C |
| 10 | `+0x1c` | Power component B |
| 11 | `+0x1e` | Height |
| 12 | `+0x20` | Acceleration penalty (`100 - value` is used) |
| 13 | `+0x22` | Running component |
| 14-25 | `+0x24`-`+0x3a` | Twelve pitch ratings |
| 26 | `+0x3c` | Birth month |
| 27 | `+0x3e` | Birth day |
| 28 | `+0x40` | Gender (`1` female, `2` male) |
| 29 | `+0x42` | Bat hand (`1` right, `2` left) |
| 30 | `+0x44` | Throw hand (`1` right, `2` left) |

Pitch indices 14-17 are Fastball, Screwball, Curveball, and Changeup. Indices 18-25 are the eight
power-pitch slots. The individual stats records contain no names for those slots, so the editor
keeps their numeric type IDs visible rather than guessing.

The eight clone-only values are loaded at object offsets `+0x48` through `+0x56`. Clone slot 2 is
confirmed as the body-height class by `SetCloneData` at `0x0014e070`. The other seven slots are
exposed as raw appearance slots until their exact cosmetic enums are confirmed.

## Derived ratings and executable references

- Power is common value 1 (`GetPowerStat`, `0x0014e420`).
- Contact averages values 3, 4, and 8 (`GetContactStat`, `0x0014e3b0`).
- Fielding averages values 2, 6, and 9 (`GetFieldingStat`, `0x0014e280`).
- Running is value 7 (`GetRunningStat`, `0x0014e260`).
- Pitching averages the first four pitch ratings, caps that intermediate result at 100, then
  averages it with value 5 (`GetPitchingStat`, `0x0014e2f0`).
- Pitch lookup uses `object + 0x24 + pitchType * 2` (`GetPitchSkill`, `0x0014eaa0`).

The retail records use 0-100 for ordinary skills. The editor deliberately accepts the entire
signed 16-bit range for experimentation. Extreme or negative values can produce odd game logic;
the timestamped archive backup is the recovery point.
