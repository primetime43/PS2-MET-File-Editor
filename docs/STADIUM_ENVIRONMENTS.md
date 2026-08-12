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

The view menu also includes the executable-derived gameplay batting POV and the normal orbit view.
Camera previews support mouse-look, WASD movement, Q/E height adjustment, and Shift for faster
movement. The third HPR component is roll; the lightweight software preview currently shows heading
and pitch but does not simulate roll.

The ambient overlay is still a preview: spline motion is implemented, but assigned skeletal ANM
motion, particles, movies, event triggers, and exact game-side random timing are not simulated.
Collision responses and those runtime behaviors still require the game. The RWS contains the static
stadium mesh; the editor adds resolved ambient DFFs and movement guides for visualization.

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

Saving creates one timestamped `DATA.MET` backup. If edited text grows beyond its original entry,
the shared MET rebuilder moves subsequent entries on 2048-byte boundaries and updates offsets. A
failed multi-stadium save restores the original archive from the backup.
