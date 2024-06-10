The tool can open, view, modify, import, and export files within the .MET files of Backyard Baseball 2004 for the PlayStation 2.

# Features
- Open .MET Files: Easily open .MET files to explore their contents.
- View Files: Browse and view files within the .MET archive.
- Modify Files: Edit the contents of files within the .MET archive.
- Import/Export Files: Import new files into the .MET archive or export existing files to your computer.
- Save Changes: Save modifications to the .MET file, ensuring your changes are applied.

To access the .MET files, you need to extract the game files from the iso using Winrar or 7zip. 

If you modify files, you need to save the modified file to .MET (Under the Edit tab is a Save File Changes) option. You can then take the modified .MET file and repack it back into the .ISO; which can be done using ImgBurn to repack the game files into an .ISO and boot up the modified game.
(This may support more games, but I haven't found another game that uses a .MET file yet)

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

![image](https://github.com/primetime43/PS2-MET-File-Editor/assets/12754111/5ada88d4-6ab9-448b-ad12-665afef58d7f)

![image](https://github.com/primetime43/PS2-DATA-File-Extractor/assets/12754111/c5129d59-4717-4597-8813-c75f153bbe80)

![image](https://github.com/primetime43/PS2-DATA-File-Extractor/assets/12754111/72400390-955e-49ac-a906-50a67b3bb657)

![image](https://github.com/primetime43/PS2-DATA-File-Extractor/assets/12754111/ba08e6b8-5240-4f45-beff-b43f046b1842)

![image](https://github.com/primetime43/PS2-DATA-File-Extractor/assets/12754111/5573ac78-c8de-4b5e-8d85-621f2279bc8d)

![image](https://github.com/primetime43/PS2-DATA-File-Extractor/assets/12754111/20a5ce20-61c0-4f00-9efc-dff3e9e55357)

![image](https://github.com/primetime43/PS2-DATA-File-Extractor/assets/12754111/ef1bb3f2-fe3e-4b43-9600-8c4270e83d2a)

![image](https://github.com/primetime43/PS2-MET-File-Editor/assets/12754111/00792048-b0a0-462f-972e-70bb9771dd8d)
