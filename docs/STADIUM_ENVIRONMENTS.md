# Stadium environment data (`fielddata.txt`)

Backyard Baseball stores environment scripting for 15 stadium variants under
`data/fields/<stadium>/fielddata.txt` in `DATA.MET`. The files are ASCII token streams with three
block types:

```text
field { ... }
collision { ... }
amb { ... }
```

The stadium editor changes recognized directive values in place. Comments, blank lines, repeated
animation directives, unusual spacing, and unrecognized lines remain byte-for-byte unchanged.

## Joined live stadium preview

Selecting a stadium also loads its matching textured RenderWare `.rws` scene beside the editor.
The split bar can resize the editor and preview, and **Open Large Preview...** opens the same scene
in its own navigable window.

The preview applies these edits immediately after a grid value is committed:

- `ambLight` changes the rendered red, green, and blue light multipliers.
- `camPos` and `camHpr` update the **Fielddata camera** view.
- `commPos` and `commHpr` update the **Commentator camera** view.
- Ambient `model` and `particle` DFF assets are resolved from their `path`, placed at `pos` or
  `relPosHpr`, and rotated with `hpr`.
- Retail `.spl` movement paths are drawn over the stadium. A path-driven model is placed at its
  first path point so its starting location is visible.
- Selecting an ambient in the list, grid, or preview highlights its model and path. Ambients beyond
  `numAmbs` can be included as translucent disabled objects.
- Only the selected movement path is shown by default. **All movement paths** enables the complete
  overlay, while the detached preview provides independent marker visibility and Off/Selected/All
  path modes so field POV views stay readable.
- A selected spline ambient can be played, paused, stopped, looped, or scrubbed. The model and its
  marker move by distance along the path, and **Face path** turns the model toward the current path
  tangent. The preview cycle uses the ambient's `speed` value, or the midpoint of `randFloatSpeed`,
  with an additional 0.25x–4x viewing-rate control. Open detached previews remain synchronized.
- The **Animation** selector resolves every `anim`, `animOnce`, `hrAnim`, `hrAnimOnce`, and
  `hrAnimOnly` slot against the selected ambient's exact DFF. Compatible RenderWare skins deform in
  both previews while the object moves along its spline. **Sync ANM to path** stretches one animation
  cycle across the path preview; disabling it plays at the ANM's native timing. **Loop ANM** can be
  changed independently and defaults off for `animOnce`/`hrAnimOnce`. Missing files, skeleton-track
  mismatches, and unsupported unskinned models are reported beside the selector instead of guessed.
- The waypoint editor exposes every retail `.spl` point as exact X/Y/Z coordinates. Points can be
  selected in the table or clicked in either 3D preview, edited, inserted between neighbors,
  duplicated, deleted, or reordered. A spline retains at least two points, and **Reset This Path**
  restores its original archive data without affecting other unsaved paths.

## Creating and cloning ambient objects

The **Ambient Objects** tab can make structural changes to complete `amb { }` blocks:

- **New Object...** selects a DFF from the archive (stadium assets are sorted first), optionally selects an ANM verified against
  that model's HAnim track count, and optionally assigns an existing SPL movement path. Animation
  behavior can be `anim`, `animOnce`, `hrAnim`, `hrAnimOnce`, or `hrAnimOnly`.
- **Clone Selected** duplicates the complete rendered block, including unknown modded directives and
  inline comments, then gives the clone its own label.
- **Copy to Stadium...** preserves the source asset path and directives while inserting the clone into
  another stadium's loaded ambient range.
- **Delete Selected** removes the complete block. These operations remain unsaved until the main
  **Save Stadiums to DATA.MET** button is used.
- The six **Placement** values expose position and heading/pitch/roll directly. Editing them adds or
  updates `pos` and `hpr` and immediately rebuilds the textured 3D preview. A newly created object with
  an SPL starts at that path's first waypoint; changing its position later creates a fixed `pos`
  override.

New objects are inserted immediately after the currently loaded ambient range and `numAmbs` increases
by one. This matters for Drive-In day and night: their existing intentionally unloaded tail block stays
unloaded instead of being enabled as an accidental side effect. Deleting an unloaded block likewise
does not change the loaded count.

The view menu also includes the executable-derived gameplay batting POV and the normal orbit view.
Camera previews support mouse-look, WASD movement, Q/E height adjustment, and Shift for faster
movement. The third HPR component is roll; the lightweight software preview currently shows heading
and pitch but does not simulate roll.

The ambient overlay is still a preview: spline and compatible skeletal ANM motion are implemented,
but particles, movies, event triggers, animation blending/selection probabilities, and exact game-side
random timing are not simulated. Home-run animations can be selected manually; the preview does not
simulate the home-run trigger. Collision responses and those runtime behaviors still require the game.
The RWS contains the static stadium mesh; the editor adds resolved ambient DFFs and movement guides
for visualization.

## Loader behavior recovered in Ghidra

`BaseballField::LoadField` at `0x0013e520` constructs the selected field path, appends the
`fielddata.txt` suffix, opens it through `vkASCIIFile`, and parses the blocks in this order:

1. `field` through `BaseballAmbients::parseFieldData` at `0x001282b0`.
2. `collision` through `BaseballAmbients::parseCollisionData` at `0x00126b50`.
3. Exactly `numAmbs` consecutive `amb` blocks through `BaseballAmbients::parseFieldAmbs` at
   `0x00129050` and `parseAmbData` at `0x00126cc0`.

The shared block dispatcher is `BaseballAmbients::parseDataSection` at `0x001284a0`.

### `field` directives

| Directive | Loader behavior |
| --- | --- |
| `numAmbs` | Number of `amb` blocks the executable allocates and reads |
| `camPos` | Team-photo/camera position vector |
| `camHpr` | Team-photo/camera heading, pitch, and roll |
| `commPos` | Commentator position vector |
| `commHpr` | Commentator heading, pitch, and roll |
| `ambLight` | Four-component ambient-light value read from the file |

The retail Drive-In day and night files contain one more `amb` block than their declared counts
(17 versus 16, and 27 versus 26). Because the loader loops strictly to `numAmbs`, those last blocks
are normally ignored. The editor labels them **not loaded** and provides an explicit button to set
the count to all blocks if a modder wants to enable them.

### `collision` directives

The parser recognizes `homerun`, `noncollidable`, `invisible`, `environment`, and `water`. Their
values are collision-material tags such as `HR` or `WT` that are registered with
`BaseballField`.

### `amb` directives

Ambient blocks describe spectators, animals, vehicles, particles, sky objects, home-run events,
and crowd systems. Retail files use these main groups:

- Assets: `path`, `model`, `movie`, `particle`, `texture`, `spline`.
- Transform/movement: `pos`, `hpr`, `relPosHpr`, `speed`, `randFloatSpeed`.
- Animation: repeated `anim`, `animOnce`, `hrAnim`, `hrAnimOnce`, and `hrAnimOnly` directives.
- Particles/events: `particleActive`, `ballSplash`, `startColor`, `endColor`, `hrDelay`,
  `hrParticleOnceOnly`, and `hrSfx`.
- Crowd data: `crowdFile`, `crowdPath`, `crowdTexture`, `crowdTextureAlpha`, `crowdRowCol`,
  `crowdDensityUV`, `crowdHeight`, `crowdCheerTime`, and `crowdLoad`.

Animation values intentionally contain an internal semicolon, for example
`anim plane.anm; 1.0 2.0;`. The editor treats the final semicolon as the directive terminator and
preserves internal separators.

## Safety

Saving writes changed `fielddata.txt` and `.spl` entries together under one timestamped `DATA.MET`
backup. Spline serialization preserves the unknown header and any suffix data, updates the RenderWare
chunk length and point count, and writes XYZ as little-endian floats. If edited text or an added
waypoint grows beyond its original entry, the shared MET rebuilder moves subsequent entries on
2048-byte boundaries and updates offsets. A failed multi-entry save restores the original archive
from the backup.
