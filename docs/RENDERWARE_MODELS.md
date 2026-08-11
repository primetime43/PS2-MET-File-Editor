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

- Drag with the left mouse button to orbit.
- Use the mouse wheel to zoom.
- Double-click the preview or select **Reset View** to restore the default camera.
- **Wireframe** makes BSP sector density and overlapping geometry easier to inspect.

The solid renderer uses resolved PNG textures when a DFF material has a matching archive texture.
Stadium RWS files keep their textures in PS2 Graphics Synthesizer raster data inside the `0x23`
dictionary. Their material-to-texture names are parsed and exported, but the swizzled embedded raster
pixels are not decoded or rewritten yet; the stadium preview therefore uses material colors.

## Export actions

- **Export Raw...** writes the selected DFF/RWS byte-for-byte.
- **Export OBJ...** writes Wavefront OBJ geometry plus its MTL material file. Vertex positions,
  normals, UVs, object/sector boundaries, material assignments, and frame transforms are preserved.
- **Export Textures...** writes resolved archive PNG textures for the selected DFF.
- **Export Texture Map...** writes a CSV of mesh/sector, material index, RGBA color, texture name,
  and resolved source. This is useful for matching a stadium material to its embedded raster before
  PS2 raster decoding is implemented.

OBJ is an interchange export. Importing an arbitrary OBJ is not currently safe because RenderWare
world rebuilding must also regenerate BSP partitioning, material windows, plugins, collision data,
and PS2-native texture/raster state.

## Reverse-engineered structures

Chunk headers are 12 bytes: 32-bit chunk ID, payload length, and RenderWare build/version value.
The retail streams use version value `0x1803FFFF`.

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
legacy fields, followed by attribute arrays controlled by the World flags. Pre-3.6 polygons are four
16-bit values in `material, vertex0, vertex1, vertex2` order.

This parser and exporter are the foundation for later geometry replacement and map cloning. Safe
editing should be added only with BSP rebuilding and full PS2 texture-dictionary support, rather than
copying raw RWS chunks and producing a scene the game cannot stream.
