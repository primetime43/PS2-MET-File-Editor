# 3D player appearances

The **3D Player Appearance Editor** uses the same reversed DFF/HAnim/ANM pipeline as the Animation
Viewer, but organizes the results around the player roster and the textures attached to each model.
The validated USA `DATA.MET` exposes editable model contexts for 48 named retail players.

## Models and preview

The editor matches each non-clone `data/kids/stats/*_stats.dat` code to resolved models in:

- `data/batting`;
- `data/fielding`;
- `data/baserunning`;
- `data/playercard`;
- `data/kids` interview assets.

Each model/context has an animation dropdown. The preview applies that ANM to the skinned DFF and uses
its paired EVT to select numbered eye and mouth textures. Drag rotates the camera, the mouse wheel zooms,
and the transport controls play or scrub the selected motion.

Clone players are not presented as independent DFFs because the game assembles them from shared body
parts and the clone appearance indices stored in their stats record. Those indices remain editable in
the normal Player Editor.

## Texture export and replacement

The texture list shows the RenderWare material name, required dimensions, archive source path, and an
asterisk for a staged replacement. Export writes the current PNG bytes, including an unsaved staged
replacement when one exists.

Replace accepts PNG, BMP, JPEG, or JPG. The image is decoded, stretched to the exact original width and
height, sampled with mirrored edges to avoid transparent border seams, and encoded as a 32-bit PNG.
The normal asset validator checks the PNG structure and dimensions before it can be staged. Every cached
model referencing the same archive PNG is updated immediately, so shared textures change consistently
in the preview.

Eye and mouth textures often have numbered variants such as `.001`, `.002`, and `.003`. They appear as
separate entries and can be edited individually; choose or scrub an animation whose EVT activates that
pose to see it on the model.

## Saving and recovery

Replacements remain in memory until **Save Textures to DATA.MET** is selected. All staged PNGs are written
as one batch after creating one timestamped `DATA.MET` backup. If a PNG grows beyond its archive slot,
the archive is rebuilt with its normal alignment rules. A failure restores the backup. **Reset Texture**
restores one staged PNG, while **Reset All Unsaved Textures** restores every staged image.

This editor changes material textures, not DFF vertices, UV coordinates, bone weights, or mesh topology.
