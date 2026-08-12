# Field cameras and player spawn coordinates

The stadium RWS geometry uses a shared field-relative coordinate system: Y is up, home plate is
`(0, 0, 0)`, and center field extends toward negative Z. These coordinates were recovered from the
retail USA `SLUS_208.65` with Ghidra 12.0.3 and validated against every main stadium RWS scene.

## Gameplay camera routines

| Function | Address | Position | Heading / pitch | Notes |
| --- | ---: | --- | --- | --- |
| `BatterCam` | `0x0013AEF0` | `(±537.6, 118.6, -727.3)` | Set later by look-at logic | X depends on batting handedness |
| `PitcherCam` | `0x0013B740` | `(485.5, 85.7, -189.0)` | Set later by look-at logic | Preview aims the exact position toward home |
| `SetInfieldPositionCam` | `0x0013B470` | `(-1007.85, 3274.28, 1817.1)` | `(-160.3°, -47.8°)` | High infield placement view |
| `SetFielderPositionCam` | `0x0013B560` | `(0, 1480, -32)` | `(-180°, -30°)` | High fielding placement view |
| `SetBattingView` | `0x0013C160` | `(0, 75.2, 509.1)` | `(180°, 1.1°)` | Normal gameplay batting view |

`LookAtPlayer` at `0x00139F80` targets player position plus 80 Y units. `LookAtBall` at
`0x00139E30` targets the live ball position. This explains why some position-setting routines do not
also contain fixed HPR values.

The `camPos` and `camHpr` values in each stadium's `fielddata.txt` are not these gameplay cameras.
`SetCommentatorCam` at `0x0013B320` obtains those values through `GetTeamPhotoPos` and
`GetTeamPhotoHpr`; they are the stadium-specific team-photo/commentator presentation anchor.

## Bases

The retail static initializers construct the common base vectors repeatedly for the player and AI
systems:

| Marker | Position |
| --- | --- |
| Home plate | `(0, 0, 0)` |
| First base | `(814.5, 0, -848)` |
| Second base | `(0, 0, -1696)` |
| Third base | `(-814.5, 0, -848)` |

The field POV base presets add 105 Y units as an inspection eye height; that offset is an editor
convenience and is not stored as a retail player spawn.

## Fielder placement tables

`__sinit_BaseballPlayer.cpp` at `0x005E6250` populates two BSS tables at startup. Entries are X/Z
pairs; player Y is resolved against field terrain at runtime.

- `sInfieldPositions` at `0x00663C30`: 24 placements organized into retail defensive layouts.
- `sOutfieldPositions` at `0x00663D50`: 27 placements, three successive nine-position layouts.

The exact values live in `BackyardFieldCoordinates.InfieldSpawns` and `OutfieldSpawns`. Representative
infield values include `(900,-1050)`, `(500,-1700)`, `(-900,-1050)`, and `(-500,-1700)`.
Representative outfield values include `(-1650,-2100)`, `(-350,-3100)`, and `(1100,-2400)`.

## Repeating the analysis

`tools/ghidra/DumpBackyardCamera.java` is a headless Ghidra post-script that exports the matching
camera and position functions plus position-table symbols and references. It intentionally reads
the user's legally obtained executable and does not modify it.
