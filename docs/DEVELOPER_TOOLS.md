# Developer tools and hidden settings

**Game Tools → Developer Tools** exposes developer settings that were shipped with Backyard Baseball
but have no normal retail menu. Runtime options and executable modes are separate because they live in
different files and can be saved independently.

The implementation was recovered from the retail USA `SLUS_208.65`. Executable patches are rejected
unless every controlled instruction matches either the original retail bytes or a patch produced by
this editor.

## Runtime options in DATA.MET

The Runtime Options tab edits `data/options/debugoptions.ini` while preserving its comments, formatting,
and unknown keys. The retail executable loads 26 of the 27 shipped entries:

| Section | Settings | Effect |
| --- | --- | --- |
| `AI` | `SwingingOff` | Prevents AI batters from swinging |
| `Catches` | `AlwaysCatch`, `AlwaysMiss` | Forces successful or failed catch attempts |
| `Batting` | `SwingLock`, `LockAngle` | Locks user swing contact and angle |
| `Batting` | `TypeLock`, `BatType` | Locks the AI bat-type enum |
| `Batting` | `StanceLock`, `Stance` | Locks the AI batting-stance enum |
| `Batting` | `NeverMiss` | Gives the AI perfect swing aim and timing |
| `Pitching` | `ErrorOff` | Removes normal pitching error |
| `PrintStatus` | ten status flags | Emits player, ball, game, simulation, catch, throw, and miscellaneous diagnostics |
| `Misc` | `HomeTeamBatsFirst` | Reverses the normal first batting side |
| `Misc` | `DisablePlayTimer` | Prevents the play clock from expiring |
| `Misc` | `LoadAmbients`, `AudioFlag` | Controls ambient loading and the debug-controlled audio path |
| `Misc` | `GamepadType1`, `GamepadType2` | Selects retail controller mapping 0 or 1 |

`AssertsEnabled` exists in the INI but the retail loader does not read it. It is displayed in gray for
reference and deliberately cannot be changed in this editor. Status output is normally visible in the
PCSX2 console/log rather than on screen.

Do not enable `AlwaysCatch` and `AlwaysMiss` together. The editor leaves both accessible because that is
how the retail configuration is structured, but the result is not a useful gameplay mode.

### Recovered enum choices

Numeric enum fields are displayed as named dropdowns while their original integer is written to the
INI. The names and values came from the retail ELF's CodeWarrior debug records:

| Setting | Exposed values |
| --- | --- |
| AI bat type | `-1` No bat selected; `0` Bunt; `1` Grounder; `2` Line drive; `3` Power; `4` Jumping Bean; `5` Butterfingers; `6` Sonic Boom; `7` Geyser; `8` Pinata; `9` Rubber; `10` Lightning; `11` Aluminum; `13` Power-up bat; `14` Super bat; `15` Best bat 1; `16` Best bat 2; `17` Random bat; `18` Do not swing |
| AI stance | `-1` Unselected; `0` Left; `1` Normal; `2` Right |
| Gamepad type | `0` Gamepad control; `1` Digital gamepad control |

`EBatType` value `12` is the internal `kBatTypeCount` marker, not a selectable bat. The engine's shared
`EControllerType` also names mouse and keyboard backends, but only the two PS2 gamepad choices are
offered. If a modded archive already contains a different numeric value, the editor displays it as an
unknown/custom value and preserves it unless the user selects a known choice.

## Dormant executable modes

The Executable Modes tab automatically looks for `SLUS_208.65` beside the opened `DATA.MET`, or accepts
one selected manually. It can enable:

- one-inning games;
- CPU control of season games;
- forced user-team wins or losses;
- an experimental exact-hit override for every batted ball.

The exact-hit override supplies the ball origin and velocity returned to the batting code. `Y` is up,
center field is negative `Z`, and positive `X` moves toward right field. The presets provide usable
starting values for a center-field drive, high fly ball, and right-field drive. These are raw game-space
values, so extreme velocities can produce unusual collision or camera behavior.

## Recovered executable addresses

The table uses runtime addresses from the R5900 Ghidra image and their matching ELF file offsets.

| Retail getter or storage | Runtime address | File offset | Editor behavior |
| --- | ---: | ---: | --- |
| one-inning flag getter | `0x001A9460` | `0x000A94E0` | Returns retail field or forced `1` |
| hit-origin getter | `0x001A9480` | `0x000A9500` | Returns retail object field or fixed vector storage |
| hit-trajectory getter | `0x001A9490` | `0x000A9510` | Returns retail object field or fixed vector storage |
| cheat-hit flag getter | `0x001A94A0` | `0x000A9520` | Returns retail field or forced `1` |
| user result-cheat getter | `0x001A94B0` | `0x000A9530` | Returns retail field, force-win `1`, or force-loss `2` |
| CPU-season flag getter | `0x001A94C0` | `0x000A9540` | Returns retail field or forced `1` |
| fixed hit-origin vector | `0x001A8990` | `0x000A8A10` | Three little-endian IEEE-754 floats when enabled |
| fixed hit-velocity vector | `0x001A89A0` | `0x000A8A20` | Three little-endian IEEE-754 floats when enabled |

The two vector locations are 16-byte retail no-op stubs for `UpdateDebugMenus` and `OpenDebugMenus`.
Both functions are only `jr ra; nop` in this build, so there is no complete hidden visual debug menu to
turn on. The editor uses those mapped executable bytes only while exact-hit mode is active and restores
their original 16-byte stubs when that mode is disabled.

## Saving, restoring, and compatibility

Saving runtime options creates a timestamped DATA.MET backup through the normal archive rebuilder.
Applying executable modes creates a separate timestamped `SLUS_208.65` backup. **Restore Retail
Developer Modes** restores only addresses in the table; content-unlock and home-run diagnostic patches
elsewhere in the executable remain untouched.

The executable patch targets the USA release only. A different region, revision, or conflicting patch is
stopped before any backup or write occurs. After changing the executable, rebuild the ISO so the patched
`SLUS_208.65` is included.
