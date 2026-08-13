# Backyard Baseball PS2 research and technical reference

This is the project-wide summary of what has been learned about the retail USA release of
Backyard Baseball for PlayStation 2. It connects the archive measurements, executable analysis,
game-data formats, and editor behavior documented throughout this repository.

The focused documents linked below remain the authoritative source for exact byte layouts,
addresses, validation rules, and editor limitations. This page is the best starting point for
understanding how the pieces fit together.

## Scope and evidence

Research and compatibility work target these legally extracted retail USA files:

- `DATA.MET`, the main game-data archive;
- `SLUS_208.65`, the PlayStation 2 executable; and
- `Settings`, the exported memory-card options and unlock file.

Findings were established by combining:

- measurements across every relevant entry in the retail `DATA.MET`;
- byte-level parsers and round-trip tests in this repository;
- Ghidra 12.0.3 analysis using the Emotion Engine/R5900 language;
- CodeWarrior symbols and debug records retained in the retail executable; and
- comparisons between executable loader behavior and the actual archive data.

Executable addresses in the focused documents are runtime addresses from the R5900 Ghidra image.
Where a patch is supported, the matching ELF file offset and original instruction signature are
also recorded. A claim described as *editor behavior* is an implementation choice and should not
be mistaken for behavior proved to occur inside the game.

## The game's three data layers

The most important architectural finding is that not every mod belongs in `DATA.MET`.

| Layer | Examples | Persistence |
| --- | --- | --- |
| `DATA.MET` | Players, portraits, models, textures, animations, stadiums, schedules, audio, and gameplay INI files | Becomes part of the game after the modified archive is placed in the extracted disc folder and the ISO is rebuilt |
| `SLUS_208.65` | Forced unlock checks and dormant executable-only developer modes | Applies to every save or no-save session in an ISO containing the patched executable |
| Memory-card `Settings` | Normal unlock progress and options | Applies only to the edited save file |

This separation explains why changing an archive entry cannot normally unlock game content and why
editing a schedule template cannot rewrite a season already stored on a memory card.

## Verified retail inventory

These counts come from the retail USA archive used for validation.

| Item | Verified count or size |
| --- | ---: |
| `DATA.MET` size | 850,393,092 bytes |
| Archive entries | 24,759 |
| RenderWare ANM animations | 2,884 |
| EVT facial and lip-sync streams | 2,169 |
| RenderWare DFF models | 1,170 |
| RenderWare RWS scenes | 26 |
| Decoded textures embedded in retail RWS scenes | 1,154 |
| Stadium environment variants | 15 |
| Supported gameplay-tuning values | 285 |
| Season schedule templates | 40 |
| Matching MIH/MIB audio pairs | 93 |
| Editable named-player 3D model contexts | 48 |
| Usable stored player biographies | 49 |
| Logical roster portraits | 61 |
| Packed portrait pages | 73 |
| Clone player-stat records | 178 |

The animation pass decoded 683,783 keyframes and reconstructed 64,926 implicit tracks. A compatible
DFF/HAnim pose preview resolves for 2,883 of the 2,884 ANM files. Conservative filename and folder
matching identifies 1,026 unambiguous ANM/EVT pairs.

## DATA.MET archive

`DATA.MET` begins with an 8-byte global header. The first word is the absolute data-section offset;
the second is the data-section size. Packed directory records start at byte 8 and contain an absolute
payload offset, logical payload size, path length, and ASCII path. The retail loader reads only the
low byte of the four-byte path-length field, limiting valid paths to 255 bytes.

Every retail payload begins on a 2,048-byte disc-sector boundary. Growing an entry safely therefore
requires more than changing its size: later payloads must remain sector aligned, later directory
offsets must move by the same sector-rounded delta, and the global data-section size must be updated.
The editor preserves untouched directory and payload bytes and creates a timestamped backup before a
write. See [MET format](MET_FORMAT.md) for the measured header values, recovered loader functions,
record layout, and resize rules.

## Players, biographies, and portraits

Player-stat entries live under `data/kids/stats/`. Their numeric prefix consists of 31 signed
little-endian 16-bit values, followed by comma-delimited names. Clone records add eight appearance
selectors. The executable confirms the formulas used for the displayed Power, Contact, Fielding,
Running, and Pitching ratings; ordinary retail values are generally 0–100 even though the stored
type can represent a wider experimental range.

Biographies are separate counted ASCII line files. Flat roster portraits are also separate from the
stats and from the interactive player-card models. Sixty-one logical portraits are packed into 73
shared 256-by-256 pages, and a single page can contain regions belonging to several players. Safe
replacement must update both the clean source PNG and all mapped regions without disturbing neighboring
pixels. Player-selection videos are 256-by-256 PSS program streams.

The game assembles the 178 clone appearances from shared assets at runtime. Clone selector 2 is
confirmed as the body-height class; the meaning of the other seven selector fields remains unconfirmed.
See [player stats and portraits](PLAYER_STATS.md) and
[3D player appearances](PLAYER_APPEARANCE.md).

## RenderWare models, stadiums, and animations

The game uses RenderWare throughout its visual data:

- DFF clumps contain frame hierarchies, geometry, materials, skin data, and HAnim mappings.
- RWS streams combine stadium worlds, clumps, and embedded texture dictionaries.
- ANM files store skeletal keyframes in standard scheme 1 or compressed scheme 2.
- EVT streams drive eye poses, mouth poses, and spoken-dialogue visemes alongside animation.

ANM files contain no bone names or explicit track table. Tracks are reconstructed from root frames
and validated previous-frame links, then mapped to HAnim node IDs from a compatible DFF. This is why
the editor labels raw animation channels as `Track 0`, `Track 1`, and so on instead of inventing names.
Animation replacement requires the recovered HAnim IDs and parent hierarchy to match, not merely the
same track count.

EVT data has a notable retail quirk: 560 batting streams omit the opening `<` on two record types.
The editor preserves that shipped pseudo-XML instead of normalizing it. The archive also contains
1,608 talkie streams paired with VAG dialogue and one commentator blink stream.

See [RenderWare models](RENDERWARE_MODELS.md), [ANM animations](ANM_ANIMATIONS.md),
[EVT facial events](EVT_FACIAL_EVENTS.md), and [asset replacement](ASSET_REPLACEMENT.md).

## Stadium space, cameras, and home-run logic

Stadium geometry shares a field-relative coordinate system: Y is up, home plate is `(0, 0, 0)`, and
center field extends toward negative Z. First, second, and third base are approximately
`(814.5, 0, -848)`, `(0, 0, -1696)`, and `(-814.5, 0, -848)`.

Each of the 15 stadium variants has a `fielddata.txt` containing field, collision, and ambient-object
directives. The matching RWS supplies the static field geometry. Ambient DFF models can use SPL paths
and compatible ANM motion. The `camPos` and `camHpr` fielddata values are presentation/commentator
anchors, not the normal gameplay batting or pitching cameras; those camera positions are built by
separate executable routines.

A home-run collision directive names an RWS material rather than storing a distance. Polygons assigned
that material form the actual sloped 3D trigger surface. Retail stadiums keep those polygons in separate
embedded clumps, allowing the editor to move or scale the boundary without changing visible stadium
geometry or topology.

See [stadium environments](STADIUM_ENVIRONMENTS.md) and
[field cameras and coordinates](FIELD_CAMERAS.md).

## Teams, schedules, and seasons

The six MLB division lists are stored in the `[Season]` section of `menuoptions.ini`. They use stable
team IDs 0–29 and contain separate active and inactive lists. The game builds a season league from
those lists when a new season is created.

Schedule templates are a separate format. The retail archive has twenty 18-game templates and twenty
32-game templates. Each round contains 12 matchups covering 24 generated season slots exactly once.
Those values are slot IDs, not permanent MLB franchise IDs, and the two stored participants are not
proved to be explicit home/away fields. Existing memory-card seasons contain their own runtime league
and result data, so archive changes apply only to newly created seasons.

See [team and league setup](TEAM_LEAGUE_SETUP.md) and
[season schedules](SEASON_SCHEDULES.md).

## Gameplay settings, developer modes, and unlocks

Gameplay behavior is distributed across retail INI files for ball physics, bats and special hits,
field surfaces, simulation, practice/debug behavior, and menu defaults. The editor currently exposes
285 typed values while preserving comments, formatting, unknown keys, and optional missing files.

The retail game also ships 27 entries in `debugoptions.ini`; the executable reads 26 of them. Confirmed
options include forced catch/miss behavior, AI swing and batting locks, pitching error control, status
logging, play-timer control, ambient loading, and controller mappings. Additional dormant modes require
guarded executable patches for one-inning games, CPU-controlled season games, forced season results,
and exact batted-ball trajectories. The retail `UpdateDebugMenus` and `OpenDebugMenus` functions are
only no-op stubs, so there is no complete hidden visual debug menu to enable in this build.

Normal unlock progress is stored in the memory-card `Settings` file as a bit mask protected by CRC-32.
The executable patch safely forces selected mask bits during unlock checks and can separately force the
Aquadome requirement complete. Executable changes accept only the verified USA `SLUS_208.65` or a
recognized patch state.

See [gameplay tweaks](GAMEPLAY_TWEAKS.md), [developer tools](DEVELOPER_TOOLS.md), and
[unlocks and save format](UNLOCKS.md).

## Audio and video

The archive uses standalone big-endian `VAGp` clips and paired little-endian MIH/MIB multistream audio.
Both contain PlayStation ADPCM frames. The editor can decode them directly to PCM WAV without FFmpeg,
or export the original bytes. All 93 retail MIH/MIB pairs were used to validate the multichannel parser.

PSS files are MPEG program streams used for player-selection animations and other game video. Replacing
a recognized PNG, BMP, VAG, or PSS asset invokes format-specific validation before any archive write.
See [PS2 audio](PS2_AUDIO.md) and [asset replacement](ASSET_REPLACEMENT.md).

## Known limits and open questions

- Executable patches target the verified USA `SLUS_208.65`; other regions and revisions are not assumed compatible.
- Seven of the eight clone appearance selectors still lack confirmed cosmetic enum meanings.
- ANM tracks do not carry trustworthy bone names; the editor exposes numeric tracks and recovered HAnim mappings.
- One anomalous Achmed fallback ANM has no matching 24-node DFF in the retail archive.
- PS2-native RenderWare geometry without ordinary vertices and UVs can require a skeleton or material-color fallback.
- Stadium particles, movies, collision simulation, animation blending, and exact random timing still require the game.
- Schedule participant order is preserved as Team A/Team B because a separate home/away meaning has not been proved.
- Editing `DATA.MET` cannot retroactively change a season or ordinary unlock progress already serialized on a memory card.

## Focused documentation index

| Area | Document |
| --- | --- |
| Archive layout and safe resizing | [MET format](MET_FORMAT.md) |
| Player records, biographies, portraits, and PSS selection videos | [Player stats](PLAYER_STATS.md) |
| Animated player models and texture editing | [Player appearance](PLAYER_APPEARANCE.md) |
| DFF/RWS parsing, previews, textures, and export | [RenderWare models](RENDERWARE_MODELS.md) |
| ANM schemes, tracks, timing, pose preview, and replacement | [ANM animations](ANM_ANIMATIONS.md) |
| EVT eye, mouth, and lip-sync events | [EVT facial events](EVT_FACIAL_EVENTS.md) |
| Stadium scripting, ambient objects, paths, and boundaries | [Stadium environments](STADIUM_ENVIRONMENTS.md) |
| Field coordinates, bases, spawns, and camera routines | [Field cameras](FIELD_CAMERAS.md) |
| Gameplay values and presets | [Gameplay tweaks](GAMEPLAY_TWEAKS.md) |
| Retail debug options and executable-only modes | [Developer tools](DEVELOPER_TOOLS.md) |
| MLB divisions and team IDs | [Team and league setup](TEAM_LEAGUE_SETUP.md) |
| Season template binary layout | [Season schedules](SEASON_SCHEDULES.md) |
| Unlock mask, executable patch, and memory-card save | [Unlocks](UNLOCKS.md) |
| VAG and MIH/MIB audio | [PS2 audio](PS2_AUDIO.md) |
| Replacement validation | [Asset replacement](ASSET_REPLACEMENT.md) |
| ISO authoring and output validation | [ISO rebuilding](ISO_REBUILD.md) |

## Additional screenshots

These earlier screenshots document the archive browser and the project's original exploration tools.

![image](https://github.com/primetime43/Backyard-Baseball-PS2-Editor/assets/12754111/5ada88d4-6ab9-448b-ad12-665afef58d7f)

![image](https://github.com/primetime43/PS2-DATA-File-Extractor/assets/12754111/c5129d59-4717-4597-8813-c75f153bbe80)

![image](https://github.com/primetime43/PS2-DATA-File-Extractor/assets/12754111/72400390-955e-49ac-a906-50a67b3bb657)

![image](https://github.com/primetime43/PS2-DATA-File-Extractor/assets/12754111/ba08e6b8-5240-4f45-beff-b43f046b1842)

![image](https://github.com/primetime43/PS2-DATA-File-Extractor/assets/12754111/5573ac78-c8de-4b5e-8d85-621f2279bc8d)

![image](https://github.com/primetime43/PS2-DATA-File-Extractor/assets/12754111/20a5ce20-61c0-4f00-9efc-dff3e9e55357)

![image](https://github.com/primetime43/PS2-DATA-File-Extractor/assets/12754111/ef1bb3f2-fe3e-4b43-9600-8c4270e83d2a)

![image](https://github.com/primetime43/Backyard-Baseball-PS2-Editor/assets/12754111/00792048-b0a0-462f-972e-70bb9771dd8d)
