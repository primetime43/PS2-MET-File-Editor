# Backyard Baseball PS2 Editor

A focused Windows modding workspace for Backyard Baseball on PlayStation 2. It provides dedicated
player, gameplay, unlock, and ISO-building tools while retaining an advanced DATA.MET browser for
raw archive work.

v0.4 (game-specific tools and player editor)
<img width="1920" height="1032" alt="image" src="https://github.com/user-attachments/assets/4ebd29d0-fe77-4be8-ac36-88f9123f96cc" />

# Features
- Open .MET Files: Easily open .MET files to explore their contents.
- View Files: Browse and view files within the .MET archive.
- Modify Files: Edit the contents of files within the .MET archive.
- Import/Export Files (right click): Import new files into the .MET archive or export existing files to your computer.
- Save Changes: Save modifications to the .MET file, ensuring your changes are applied.

To access the .MET files, you need to extract the game files from the ISO using WinRAR or 7-Zip.

## Modding and unlock editing

- **Player Editor...** on the **Game Tools** tab shows the stored player polaroids and edits all 230 retail and clone `*_stats.dat` records, including names,
  batting/running/fielding components, all 12 pitch ratings, identity data, and clone appearance slots.
  The image dropdown and arrow buttons cycle through the selected player's own assets: their static polaroid plus the Breathe, Breathe + Blink, and Pick Me selection animations when present. Polaroids accept PNG, BMP, or JPEG replacements and update the packed menu textures; animations can be exported or replaced as compatible 256-by-256 PS2 PSS files.
  The reversed layout and executable addresses are in [`docs/PLAYER_STATS.md`](docs/PLAYER_STATS.md).
- **3D Player Appearance Editor...** groups the retail batting, fielding, baserunning, player-card,
  and interview models by player. It previews the real animated skinned DFF, lists every resolved
  clothing, skin, hair, face, hat, shoe, and equipment PNG, and can export, replace, preview, reset,
  and batch-save textures. PNG, BMP, and JPEG imports are converted and resized to the original game
  texture dimensions. See [3D player appearances](docs/PLAYER_APPEARANCE.md).
- **Stadium Editor...** edits lighting, cameras, collision tags, and ambient models, particles,
  positions, animations, and speeds across all 15 `fielddata.txt` stadium variants. Loader details are
  documented in [`docs/STADIUM_ENVIRONMENTS.md`](docs/STADIUM_ENVIRONMENTS.md).
- **Gameplay Tweaks...** on the **Game Tools** tab provides validated tabs for 285 ball, bat/power-up, field physics,
  simulation, practice/cheat, and game-default values stored in `DATA.MET`.
- **Animation Viewer / Editor...** parses all 2,884 RenderWare ANM files, reconstructs their unnamed
  linked tracks and keyframes, resolves the matching DFF/HAnim hierarchy and skinned geometry for an
  interactive textured player preview, and shows matching EVT eye/mouth expressions on the same playhead,
  and safely edits duration, playback speed, or individual keyframe times. Its replacement dialog previews
  two models side by side and can copy motion into another verified-compatible animation slot, optionally
  fitting the target duration and copying its synchronized EVT expressions. Both standard and compressed
  retail schemes are supported. See [ANM animations](docs/ANM_ANIMATIONS.md).
- **Facial Event Editor...** edits all 2,169 EVT timelines. Talkie lip sync plays its paired VAG
  dialogue while the matching commentator or roster player's textured 3D model follows the retail
  mouth-shape mapping; batting eye/mouth events animate the matching player's model and actual
  numbered PNG textures from `DATA.MET`. See
  [EVT facial events](docs/EVT_FACIAL_EVENTS.md).
- **3D Model and Stadium Viewer...** catalogs all 1,170 RenderWare DFF assets and 26 RWS scenes.
  It renders rigid and skinned-model base geometry, reconstructs RWS stadium BSP world sectors and
  embedded clumps, decodes all 1,154 embedded RWS textures with material/vertex lighting and sampling
  modes, lists material assignments and stream chunks, and exports raw assets, Wavefront OBJ/MTL
  geometry, decoded PNGs, or a CSV texture map. Double-click a `.dff` or
  `.rws` in the archive browser to open it directly. See [RenderWare models and stadiums](docs/RENDERWARE_MODELS.md).
- **Unlock Game Content...** patches selected players, fields, Darts, and Aquadome unlocked in the game itself.
- The **DATA.MET Browser** tab contains the file tree, raw preview/editor, and visible Save, Import, and Export actions.
- EVT files are recognized as their original XML/pseudo-XML text. Double-click a selected EVT in the
  archive tree to open it directly in the Facial Event Editor.
- Double-click an `.anm` entry to open it directly in the Animation Viewer / Editor.
- Double-click a `.dff` or `.rws` entry to open it directly in the 3D Model and Stadium Viewer.
- Selecting a `.mih`, `.mib`, or `.vag` audio entry shows its decoded stream metadata instead of a
  generic binary-file message. **Export Selected** can write a playable PCM WAV; streamed music can
  also be exported as its original matching MIH/MIB pair. See [PS2 audio parsing and export](docs/PS2_AUDIO.md).
- **View > Hex Editor** edits any selected archive payload as validated byte pairs.
- **Import File** preserves replacement files byte-for-byte, including binary data and trailing zeros.
  PNG/BMP textures are checked for valid headers and matching dimensions; VAG audio is checked for
  valid ADPCM headers, frame layout, and sample metadata; PSS video is checked for MPEG program,
  video, picture, resolution, frame-rate, and audio-stream compatibility before `DATA.MET` is changed.
  See [asset replacement validation](docs/ASSET_REPLACEMENT.md) for the exact blocking and warning rules.
- MET, executable, and optional save-file edits create timestamped backups.

Persistent unlock progress normally lives on the memory card, not in `DATA.MET`. The executable
patch makes selected content available to every save in the rebuilt ISO. See
[Backyard Baseball unlock and save format](docs/UNLOCKS.md) for patch details and the recovered bit map.

## Workflow: Extracting, Modifying, and Rebuilding the ISO

### Step 1: Extract the ISO
1. Use **WinRAR** or **7-Zip** to extract your PS2 game ISO
2. Extract all files to a folder
3. You should see files like `SYSTEM.CNF`, `DATA.MET`, and the game executable

### Step 2: Modify the game files
1. Open `DATA.MET` in the MET File Editor and make any archive changes.
2. Select **Player Editor...** in the main window to modify player names, skills, pitch ratings, identity, or
   clone appearance values directly in the game's `*_stats.dat` records.
3. Select **Stadium Editor...** to edit field lighting, cameras, collisions, and ambient objects.
4. For structured game tuning, select **Gameplay Tweaks...**, edit values in the category
   tabs, and select **Save to DATA.MET**. Comments and unsupported INI keys are preserved.
5. Save any other MET changes; resizing and backups are handled automatically.
6. Select **Unlock Game Content...** in the main window.
7. Select the extracted USA executable `SLUS_208.65`.
8. Select individual content or **Unlock All**, then apply the patch.
9. Keep the patched executable beside the other extracted game files.

### Step 3: Rebuild the ISO in the editor

1. Install ImgBurn if it is not already installed; the editor detects the standard install location.
2. Choose **File > Rebuild Game ISO...**.
3. Select the extracted game folder and an output path outside that folder.
4. Confirm the volume label and click **Build ISO**.
5. The editor starts ImgBurn with `ISO9660 + UDF` and UDF revision `1.02`, then validates the generated image.

The source folder is checked for `SYSTEM.CNF`, `DATA.MET`, and the executable referenced by
`SYSTEM.CNF`. Existing output images are moved to timestamped backups. See
[ISO rebuilding](docs/ISO_REBUILD.md) for validation, recovery, and backend details.

# .MET File Structure
The .MET file in Backyard Baseball 2004 (PS2) contains various data and resources used by the game, such as textures, models, and other game assets. Understanding the structure of the .MET file is crucial for reading and writing its contents. Here's an overview of the .MET file structure:

# Header
The .MET file starts with a header that contains metadata about the file. This typically includes information such as the number of file entries, offsets, and sizes.

# File Entries
Following the header, the .MET file contains a list of file entries. Each file entry represents an individual file within the archive and contains the following information:

- Offset: The starting position of the file data within the .MET file.
- Size of Data: The size of the file data.
- Size of String: The length of the string representing the file path.
- File Path: The relative path of the file within the archive.

# Example Breakdown
For each file entry, the structure is as follows:

- Offset (4 bytes): The address where the data starts (e.g., 00 C0 37 2C).
- Size of Data (4 bytes): The size of the data (e.g., offset data start address + 27 0D 00 00).
- Size of String (4 bytes): The length of the string name (e.g., 16 00 00 00).
- Path String: The file path string (e.g., 64 61 74 61 2F 6D 65 6E 75 73 2F 63 72 65 64 69 74 73 2E 74 78 74).

For example with these bytes
```
00 C0 37 2C 27 0D 00 00 16 00 00 00 64 61 74 61 2F 6D 65 6E 75 73 2F 63 72 65 64 69 74 73 2E 74 78 74
```

```
Header starts at address: 1171683 (0x11E0E3)
Header ends at address: 1171717 (0x11E105)
Length of the header: 34 (0x22)
Length of the string: 22 (0x16)
Path: data/menus/credits.txt
Offset: 741851136 (0x2C37C000)
OriginalSize: 3367 (0xD27)
Data spans from 0x2C37C000 to 0x2C37CD27
```

![image](https://github.com/primetime43/Backyard-Baseball-PS2-Editor/assets/12754111/5ada88d4-6ab9-448b-ad12-665afef58d7f)

![image](https://github.com/primetime43/PS2-DATA-File-Extractor/assets/12754111/c5129d59-4717-4597-8813-c75f153bbe80)

![image](https://github.com/primetime43/PS2-DATA-File-Extractor/assets/12754111/72400390-955e-49ac-a906-50a67b3bb657)

![image](https://github.com/primetime43/PS2-DATA-File-Extractor/assets/12754111/ba08e6b8-5240-4f45-beff-b43f046b1842)

![image](https://github.com/primetime43/PS2-DATA-File-Extractor/assets/12754111/5573ac78-c8de-4b5e-8d85-621f2279bc8d)

![image](https://github.com/primetime43/PS2-DATA-File-Extractor/assets/12754111/20a5ce20-61c0-4f00-9efc-dff3e9e55357)

![image](https://github.com/primetime43/PS2-DATA-File-Extractor/assets/12754111/ef1bb3f2-fe3e-4b43-9600-8c4270e83d2a)

![image](https://github.com/primetime43/Backyard-Baseball-PS2-Editor/assets/12754111/00792048-b0a0-462f-972e-70bb9771dd8d)
