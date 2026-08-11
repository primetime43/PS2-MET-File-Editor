# Asset replacement validation

Format checks run before a recognized asset is written to `DATA.MET`. They apply to the archive
browser's **Import File** and hex-save paths as well as structured replacements that use the batch
archive writer. Unsupported extensions remain byte-for-byte imports.

| Asset | Replacement is blocked when | Confirmation warning |
| --- | --- | --- |
| PNG texture | Signature/chunks are invalid, image data is missing, or dimensions differ from the original | PNG bit depth or color type changed |
| BMP texture | Header/pixel offset is invalid, dimensions differ, or bit depth/compression is unsupported | Pixel depth changed |
| VAG audio | `VAGp` header, data size, sample rate, or 16-byte ADPCM frames are invalid | Sample rate or VAG version changed |
| PSS video | MPEG pack/system/sequence/video/picture headers are missing or dimensions differ | Frame-rate code or private audio-stream presence changed |

PNG and BMP dimensions are kept equal to the selected original texture because game layouts and
RenderWare material definitions assume those sizes. BMP validation accepts the retail formats found
in this game, including uncompressed, bitfields, RLE8, and RLE4 where valid for the pixel depth.

VAG data must use complete 16-byte PlayStation ADPCM frames. Replacement clips may have a different
duration. A different sample rate is allowed with a warning so deliberate audio mods are possible.

PSS dimensions are compared with the selected original file; they are not globally fixed. Player
selection animations are 256 by 256, while full-screen and other game videos use different sizes.
