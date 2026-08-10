# ISO rebuilding

The editor can rebuild the extracted PS2 game folder through **File > Rebuild Game ISO...**.

## Backend

The release uses an installed copy of ImgBurn as its ISO authoring backend. ImgBurn is not
redistributed with this project. The editor checks the standard 32-bit and 64-bit Program Files
locations and also lets the user browse to `ImgBurn.exe`.

The generated command uses these build settings:

- Build input mode: Standard
- Build output mode: Image file
- File systems: `ISO9660 + UDF`
- UDF revision: `1.02`
- Root-folder response: Yes
- Automatic start and close-on-success

Arguments are passed individually to the process API rather than assembled into a shell command.

## Source validation

Before ImgBurn starts, the selected directory must contain these files at its root:

- `SYSTEM.CNF`
- `DATA.MET`
- The boot executable referenced by the `BOOT` or `BOOT2` line in `SYSTEM.CNF`

The referenced executable path is resolved inside the source directory. A missing executable or
a path that escapes the source directory is rejected. The output ISO must be outside the source
directory so an image can never recursively include itself.

## Output and recovery

If the output ISO already exists and replacement is confirmed, it is moved to a timestamped
`.backup_...` sibling before building. If the build fails, a newly generated partial image is
moved to a `.failed_...` sibling and the previous image is restored.

After ImgBurn exits, the editor verifies:

1. The image exists and is large enough to contain filesystem descriptors.
2. Sector 16 contains the ISO9660 `CD001` primary descriptor identifier.
3. Sector 256 contains the UDF Anchor Volume Descriptor Pointer tag.

The build is reported successful only after these checks pass.

## Volume label

Labels are normalized to uppercase ISO9660-compatible letters, digits, and underscores and are
limited to 32 characters. The source-folder name is used when no label is supplied.
