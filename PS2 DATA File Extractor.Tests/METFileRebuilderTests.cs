using PS2_DATA_File_Extractor.FileOperations;
using PS2_DATA_File_Extractor.Models;

namespace PS2_DATA_File_Extractor.Tests;

public sealed class METFileRebuilderTests : IDisposable
{
    private const int DataSectionOffset = 2048;
    private const int SecondEntryOffset = 4096;
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), $"ps2-met-tests-{Guid.NewGuid():N}");

    [Fact]
    public void ExpansionWithinExistingSectorPaddingDoesNotShiftFollowingEntry()
    {
        Directory.CreateDirectory(_tempDirectory);
        string sourcePath = Path.Combine(_tempDirectory, "source.met");
        string rebuiltPath = Path.Combine(_tempDirectory, "rebuilt.met");
        CreateArchive(sourcePath);

        METFileStructure original = METFileReader.ReadMETFile(sourcePath);
        FileEntry target = original.GetEntryByPath("first.bin")!;
        byte[] replacement = Enumerable.Repeat((byte)0xA5, 1024).ToArray();

        METFileRebuilder.RebuildWithExpandedEntry(sourcePath, rebuiltPath, target, replacement);

        METFileStructure rebuilt = METFileReader.ReadMETFile(rebuiltPath);
        Assert.Equal(new FileInfo(sourcePath).Length, new FileInfo(rebuiltPath).Length);
        Assert.Equal(rebuilt.TotalFileSize - rebuilt.DataSectionOffset, rebuilt.DataSectionSize);
        Assert.Equal(1024, rebuilt.GetEntryByPath("first.bin")!.OriginalSize);
        Assert.Equal(SecondEntryOffset, rebuilt.GetEntryByPath("second.bin")!.Offset);
        Assert.Equal(0x5A, ReadBytes(rebuiltPath, 1000, 1)[0]);
        Assert.Equal(replacement, ReadBytes(rebuiltPath, DataSectionOffset, replacement.Length));
        Assert.Equal(new byte[] { 9, 8, 7, 6 }, ReadBytes(rebuiltPath, SecondEntryOffset, 4));
        Assert.True(rebuilt.ValidateStructure().IsValid);
    }

    [Fact]
    public void ExpansionBeyondSectorPaddingShiftsTailByWholeSectors()
    {
        Directory.CreateDirectory(_tempDirectory);
        string sourcePath = Path.Combine(_tempDirectory, "source.met");
        string rebuiltPath = Path.Combine(_tempDirectory, "rebuilt.met");
        CreateArchive(sourcePath);

        METFileStructure original = METFileReader.ReadMETFile(sourcePath);
        FileEntry target = original.GetEntryByPath("first.bin")!;
        byte[] replacement = Enumerable.Repeat((byte)0xCC, 2050).ToArray();

        METFileRebuilder.RebuildWithExpandedEntry(sourcePath, rebuiltPath, target, replacement);

        METFileStructure rebuilt = METFileReader.ReadMETFile(rebuiltPath);
        FileEntry second = rebuilt.GetEntryByPath("second.bin")!;
        Assert.Equal(SecondEntryOffset + METFileRebuilder.SectorSize, second.Offset);
        Assert.Equal(0, second.Offset % METFileRebuilder.SectorSize);
        Assert.Equal(new FileInfo(sourcePath).Length + METFileRebuilder.SectorSize, new FileInfo(rebuiltPath).Length);
        Assert.Equal(rebuilt.TotalFileSize - rebuilt.DataSectionOffset, rebuilt.DataSectionSize);
        Assert.Equal(replacement, ReadBytes(rebuiltPath, DataSectionOffset, replacement.Length));
        Assert.Equal(new byte[] { 9, 8, 7, 6 }, ReadBytes(rebuiltPath, second.Offset, 4));
        Assert.True(rebuilt.ValidateStructure().IsValid);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private static void CreateArchive(string path)
    {
        byte[] firstData = { 1, 2, 3 };
        byte[] secondData = { 9, 8, 7, 6 };
        int totalLength = SecondEntryOffset + secondData.Length;

        using FileStream stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite);
        using BinaryWriter writer = new BinaryWriter(stream);
        writer.Write(DataSectionOffset);
        writer.Write(totalLength - DataSectionOffset);
        WriteEntry(writer, DataSectionOffset, firstData.Length, "first.bin");
        WriteEntry(writer, SecondEntryOffset, secondData.Length, "second.bin");
        writer.Write(new byte[12]);

        stream.Position = 1000;
        writer.Write((byte)0x5A);
        stream.Position = DataSectionOffset;
        writer.Write(firstData);
        stream.Position = SecondEntryOffset;
        writer.Write(secondData);
    }

    private static void WriteEntry(BinaryWriter writer, int offset, int size, string path)
    {
        byte[] pathBytes = System.Text.Encoding.ASCII.GetBytes(path);
        writer.Write(offset);
        writer.Write(size);
        writer.Write(pathBytes.Length);
        writer.Write(pathBytes);
    }

    private static byte[] ReadBytes(string path, long offset, int count)
    {
        using FileStream stream = File.OpenRead(path);
        stream.Position = offset;
        byte[] data = new byte[count];
        stream.ReadExactly(data);
        return data;
    }
}
