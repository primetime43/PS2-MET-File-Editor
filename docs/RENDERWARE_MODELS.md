# RenderWare DFF models and RWS stadium scenes

Backyard Baseball PS2 stores its 3D content as RenderWare 3.5 binary streams inside `DATA.MET`.
The retail USA archive contains exactly 1,170 `.dff` files and 26 `.rws` files. Open **3D Model and
Stadium Viewer...** from **Game Tools**, or double-click either file type in the archive browser.

## What the viewer parses

For DFF clumps the editor reads the frame hierarchy, local transforms, geometry list, atomic-to-frame
and atomic-to-geometry assignments, vertex positions, normals, vertex colors, UV coordinates,
triangles, materials, and texture names. This includes ordinary rigid props as well as the base pose
of skinned player models. The animation tools remain the right place to preview a player model with
ANM skin deformation.

RWS is a multi-object RenderWare stream rather than a separate model format. The game uses it for:

- a platform-specific PS2 texture dictionary (`0x23`);
- a static `World` (`0x0B`) containing the stadium BSP tree;
- zero or more movable/animated `Clump` objects (`0x10`);
- optional UV animation dictionaries and ANM chunks.

The World parser recursively reads plane sectors (`0x0A`) and atomic/world sectors (`0x09`). Each
leaf reconstructs its material-list window, positions, packed normals, pre-lit colors, UV sets, and
legacy RenderWare 3.5 polygons. The viewer combines those leaves with embedded clumps for a complete
scene preview and keeps the sector split visible in the **Meshes / sectors** table.

Retail validation currently finds renderable triangle data in 1,140 of the 1,196 cataloged assets.
The other 56 DFFs are primarily flyby markers and particle/effect emitters with no triangle geometry;
they remain selectable so their stream metadata and original bytes can still be inspected/exported.

## Preview controls

- Drag with the left mouse button to orbit; right-drag pans the orbit camera.
- Use the mouse wheel to zoom.
- Double-click the preview or select **Fit View** to restore and frame the visible geometry.
- **Perspective** uses a perspective-correct camera and UV interpolation; disable it for an
  orthographic inspection view.
- **Hide backdrop** removes sky-box and horizon materials so the stadium can be viewed from above.
- **Show helpers** reveals the game's `C`, `WT`, and `HR` collision/trigger helper meshes. These use
  tiny placeholder textures and are hidden by default; the tall white Boardwalk column is its `HR`
  helper volume, not missing stadium art.
- **Cull backfaces** is optional because some retail props intentionally use two-sided polygons.
- **Wireframe** makes BSP sector density and overlapping geometry easier to inspect.
- **Open in New Window...** opens the selected model or stadium in an independent, resizable window.
- The detached window's **View** list includes retail gameplay camera positions, base-level POVs,
  and the original fit/orbit camera. In a field POV, drag to look, use **W/A/S/D** to fly, **Q/E**
  to change height, hold **Shift** for faster movement, and select a movement speed from the toolbar.

The gameplay presets and fielder spawn tables were recovered from `SLUS_208.65`; see
[`FIELD_CAMERAS.md`](FIELD_CAMERAS.md) for addresses, exact coordinates, and the distinction between
gameplay cameras and each stadium's separate `fielddata.txt` team-photo camera.

The solid renderer uses resolved archive PNG textures for DFF materials and decodes the RWS file's
embedded platform-independent texture dictionary. All 1,154 retail RWS textures are supported. The
decoder reads the highest-resolution mip image, 4-bit and 8-bit RGBA palettes, 24/32-bit pixels, row
stride, material color, pre-lit vertex color, and RenderWare wrap/mirror/clamp addressing. The
**Materials and textures** table shows each unique material, image dimensions, sampling state, use
count, and exact source. Embedded texture rewriting is not implemented yet.

## Export actions

- **Export Raw...** writes the selected DFF/RWS byte-for-byte.
- **Export OBJ...** writes Wavefront OBJ geometry plus its MTL material file. Vertex positions,
  normals, UVs, object/sector boundaries, material assignments, and frame transforms are preserved.
- **Export Textures...** writes resolved archive PNG textures or every decoded embedded RWS texture.
- **Export Texture Map...** writes a CSV of mesh/sector, material index, RGBA color, texture name,
  and resolved source.

OBJ is an interchange export. Importing an arbitrary OBJ is not currently safe because RenderWare
world rebuilding must also regenerate BSP partitioning, material windows, plugins, collision data,
and PS2-native texture/raster state.

## Reverse-engineered structures

Chunk headers are 12 bytes: 32-bit chunk ID, payload length, and RenderWare build/version value.
The retail streams use version value `0x1803FFFF`.

The `0x23` texture dictionary starts with a packed texture-count/platform word (`platform = 1`).
Each texture record contains a mip count, that many `RwImage` (`0x18`) chunks, and a Texture (`0x06`)
chunk carrying its sampling flags and name. Each `RwImage` struct stores width, height, bit depth, and
row stride, followed by pixels and an RGBA palette for indexed images. This is a platform-independent
image stream rather than raw Graphics Synthesizer VRAM, which is why it can be decoded losslessly.

The 64-byte World struct used by the retail RWS files contains:

| Offset | Type | Meaning |
| ---: | --- | --- |
| `0x00` | `int32` | Root is an atomic/world sector |
| `0x04` | `float[3]` | Inverse world origin |
| `0x10` | `int32` | Total polygons |
| `0x14` | `int32` | Total vertices |
| `0x18` | `int32` | Plane-sector count |
| `0x1C` | `int32` | World-sector count |
| `0x20` | `int32` | Legacy collision-sector size |
| `0x24` | `uint32` | World flags and UV-set count |
| `0x28` | `float[6]` | World bounding box |

Each plane-sector struct stores its split axis/type, split value, child types, and child bounds. Each
world-sector struct starts with a material-window base, polygon and vertex counts, tight bounds, and
legacy fields, followed by attribute arrays controlled by the World flags. These retail streams use
the modern four-word `vertex0, vertex1, vertex2, material` triangle layout; the sector material-window
base is added to the stored material index.

This parser and exporter are the foundation for later geometry replacement and map cloning. Safe
editing should be added only with BSP rebuilding and texture-dictionary writing, rather than copying
raw RWS chunks and producing a scene the game cannot stream.
