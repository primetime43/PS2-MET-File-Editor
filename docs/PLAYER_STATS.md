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
## Player portraits

Finished roster portraits have editable source PNG entries under `data/polaroids/<player-code>.png`
in `DATA.MET`; they are not stored in `*_stats.dat`. The game also has a compiled copy: 61 logical
portraits are packed into 73 shared 256-by-256 pages named `data/menus/polaroids_0.png` through
`polaroids_72.png`. `data/menus/polaroids.imp` maps each logical portrait name and size to two or
four rectangular regions on those pages. Overflow strips and corners from different portraits can
share the same page, which is why the numbered PNGs can look like unrelated image fragments.

The player editor's image dropdown and previous/next buttons are scoped to the selected player. A retail
player can have up to four entries: the static polaroid and three 256-by-256 selection animations named
`<code>_breathe.pss`, `<code>_breatheblink.pss`, and `<code>_pickme.pss` under
`data/video/pickplayer/`. For example, Derek Jeter uses `jete.png` plus `jete_breathe.pss`,
`jete_breatheblink.pss`, and `jete_pickme.pss`. Clone records do not have dedicated fixed image entries.

The editor shows a representative frame for PSS animations when a local MPEG decoder is available.
**Export PSS...** preserves the original animation bytes. **Replace PSS...** accepts a compatible
256-by-256 PS2 PSS file and validates its MPEG program-stream and sequence headers before updating
`DATA.MET`. If a preview decoder is unavailable, PSS export and replacement remain available.

**Export...** writes the clean source PNG without alteration. **Replace...** accepts PNG, BMP, JPG,
or JPEG files, converts and fits the image to the source portrait's original dimensions without
stretching, then updates both the source PNG and every region referenced by `polaroids.imp` in one
archive transaction. Existing packed alpha is retained so the polaroid outline remains transparent,
and pixels belonging to neighboring portraits on shared pages are preserved. A single timestamped
`DATA.MET` backup is created first. Images must be between 1 and 4096 pixels in each dimension.
If any replacement PNG changes size, the shared MET rebuilder preserves 2048-byte sector
alignment and updates later offsets.

The separate `data/playercard/<player>/` folders are the models, animations, and textures used to
render the interactive 3D player-card view. `viewcard.imj` and `pickplayers.imj` load their screen UI.
Those assets are not alternate flat portraits, so they are intentionally not replaced by the polaroid
button.

The 178 `clone*_stats.dat` records have eight appearance selectors but no matching polaroid entry.
The game assembles their 3D appearance from shared assets at runtime, so the editor cannot assign
an in-game clone portrait without adding a new game reference. Barry Bonds and Eric Estrada also
have no stored polaroid in the retail USA archive.
