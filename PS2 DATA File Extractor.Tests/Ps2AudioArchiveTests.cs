using System.Text;
using PS2_DATA_File_Extractor.FileOperations;
using PS2_DATA_File_Extractor.Models;

namespace PS2_DATA_File_Extractor.Tests;

public sealed class Ps2AudioArchiveTests : IDisposable
{
    private const int MihOffset = 2048;
    private const int MibOffset = 4096;
    private const int VagOffset = 6144;
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(), $"ps2-audio-tests-{Guid.NewGuid():N}");

    [Fact]
    public void InspectAndDecodeMibMihPair()
    {
        Directory.CreateDirectory(_tempDirectory);
        string metPath = Path.Combine(_tempDirectory, "DATA.MET");
        CreateArchive(metPath, includeMih: true);
        METFileStructure structure = METFileReader.ReadMETFile(metPath);
        FileEntry entry = structure.GetEntryByPath("data/audio/test.mib")!;

        Ps2AudioInfo info = Ps2AudioArchive.Inspect(metPath, entry, structure);
        byte[] wave = Ps2AudioArchive.DecodeToWave(metPath, entry, structure);

        Assert.Equal(Ps2AudioKind.MibMih, info.Kind);
        Assert.Equal(2, info.Channels);
        Assert.Equal(22050, info.SampleRate);
        Assert.Equal(84, info.SamplesPerChannel);
        Assert.Equal(96, info.CompressedDataSize);
        Assert.Equal(32, info.InterleaveBlockSize);
        Assert.Equal(2, info.BlockCount);
        Assert.Equal(16, info.LastBlockSize);
        AssertWave(wave, channels: 2, sampleRate: 22050, dataSize: 336);
    }

    [Fact]
    public void InspectAndDecodeVag()
    {
        Directory.CreateDirectory(_tempDirectory);
        string metPath = Path.Combine(_tempDirectory, "DATA.MET");
        CreateArchive(metPath, includeMih: true);
        METFileStructure structure = METFileReader.ReadMETFile(metPath);
        FileEntry entry = structure.GetEntryByPath("data/audio/test.vag")!;

        Ps2AudioInfo info = Ps2AudioArchive.Inspect(metPath, entry, structure);
        byte[] wave = Ps2AudioArchive.DecodeToWave(metPath, entry, structure);

        Assert.Equal(Ps2AudioKind.Vag, info.Kind);
        Assert.Equal(1, info.Channels);
        Assert.Equal(22050, info.SampleRate);
        Assert.Equal(56, info.SamplesPerChannel);
        Assert.Equal("test-stream", info.StreamName);
        AssertWave(wave, channels: 1, sampleRate: 22050, dataSize: 112);
    }

    [Fact]
    public void RawPairExportPreservesBothArchiveEntries()
    {
        Directory.CreateDirectory(_tempDirectory);
        string metPath = Path.Combine(_tempDirectory, "DATA.MET");
        (byte[] mih, byte[] mib, _) = CreateArchive(metPath, includeMih: true);
        METFileStructure structure = METFileReader.ReadMETFile(metPath);
        FileEntry entry = structure.GetEntryByPath("data/audio/test.mih")!;
        string destination = Path.Combine(_tempDirectory, "exported.wav");

        Ps2AudioArchive.ExportRawPair(metPath, entry, structure, destination);

        Assert.Equal(mib, File.ReadAllBytes(Path.ChangeExtension(destination, ".mib")));
        Assert.Equal(mih, File.ReadAllBytes(Path.ChangeExtension(destination, ".mih")));
    }

    [Fact]
    public void MissingCompanionEntryIsReported()
    {
        Directory.CreateDirectory(_tempDirectory);
        string metPath = Path.Combine(_tempDirectory, "DATA.MET");
        CreateArchive(metPath, includeMih: false);
        METFileStructure structure = METFileReader.ReadMETFile(metPath);
        FileEntry entry = structure.GetEntryByPath("data/audio/test.mib")!;

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            Ps2AudioArchive.Inspect(metPath, entry, structure));

        Assert.Contains("companion", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".mih", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, recursive: true);
    }

    private static (byte[] Mih, byte[] Mib, byte[] Vag) CreateArchive(
        string path,
        bool includeMih)
    {
        byte[] mih = CreateMih();
        byte[] mib = new byte[128];
        byte[] vag = CreateVag();
        List<(string Path, int Offset, byte[] Data)> entries = new();
        if (includeMih)
            entries.Add(("data/audio/test.mih", MihOffset, mih));
        entries.Add(("data/audio/test.mib", MibOffset, mib));
        entries.Add(("data/audio/test.vag", VagOffset, vag));

        int totalLength = VagOffset + vag.Length;
        using FileStream stream = new(path, FileMode.Create, FileAccess.ReadWrite);
        using BinaryWriter writer = new(stream);
        writer.Write(MihOffset);
        writer.Write(totalLength - MihOffset);
        foreach ((string entryPath, int offset, byte[] data) in entries)
            WriteEntry(writer, offset, data.Length, entryPath);
        writer.Write(new byte[MihOffset - checked((int)stream.Position)]);
        foreach ((_, int offset, byte[] data) in entries)
        {
            stream.Position = offset;
            writer.Write(data);
        }

        return (mih, mib, vag);
    }

    private static byte[] CreateMih()
    {
        byte[] data = new byte[64];
        WriteLittleEndian(data, 0x00, 0x40);
        WriteLittleEndian(data, 0x04, (16u << 8) | 0x20);
        WriteLittleEndian(data, 0x08, 2);
        WriteLittleEndian(data, 0x0c, 22050);
        WriteLittleEndian(data, 0x10, 32);
        WriteLittleEndian(data, 0x14, 2);
        return data;
    }

    private static byte[] CreateVag()
    {
        byte[] data = new byte[48 + 32];
        Encoding.ASCII.GetBytes("VAGp").CopyTo(data, 0);
        WriteBigEndian(data, 0x04, 0x20);
        WriteBigEndian(data, 0x0c, 32);
        WriteBigEndian(data, 0x10, 22050);
        Encoding.ASCII.GetBytes("test-stream").CopyTo(data, 0x20);
        return data;
    }

    private static void AssertWave(byte[] wave, short channels, int sampleRate, int dataSize)
    {
        Assert.Equal("RIFF", Encoding.ASCII.GetString(wave, 0, 4));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(wave, 8, 4));
        Assert.Equal(channels, BitConverter.ToInt16(wave, 22));
        Assert.Equal(sampleRate, BitConverter.ToInt32(wave, 24));
        Assert.Equal(dataSize, BitConverter.ToInt32(wave, 40));
        Assert.Equal(44 + dataSize, wave.Length);
    }

    private static void WriteEntry(BinaryWriter writer, int offset, int size, string path)
    {
        byte[] pathBytes = Encoding.ASCII.GetBytes(path);
        writer.Write(offset);
        writer.Write(size);
        writer.Write(pathBytes.Length);
        writer.Write(pathBytes);
    }

    private static void WriteLittleEndian(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)value;
        data[offset + 1] = (byte)(value >> 8);
        data[offset + 2] = (byte)(value >> 16);
        data[offset + 3] = (byte)(value >> 24);
    }

    private static void WriteBigEndian(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)(value >> 24);
        data[offset + 1] = (byte)(value >> 16);
        data[offset + 2] = (byte)(value >> 8);
        data[offset + 3] = (byte)value;
    }
}
