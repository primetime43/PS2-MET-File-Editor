using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using PS2_DATA_File_Extractor.FileOperations;
using PS2_DATA_File_Extractor.Models;

namespace PS2_DATA_File_Extractor.Tests;

public sealed class PlayerPortraitPackingTests : IDisposable
{
    private const int StatsOffset = 2048;
    private const int PortraitOffset = 4096;
    private const int ImportOffset = 6144;
    private const int TextureOffset = 8192;
    private const int TextureId = 650100;
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(), $"portrait-packing-tests-{Guid.NewGuid():N}");

    [Fact]
    public void ReplacementUpdatesMappedTextureRegionAndPreservesItsAlphaAndNeighbors()
    {
        Directory.CreateDirectory(_tempDirectory);
        string metPath = Path.Combine(_tempDirectory, "DATA.MET");
        byte[] originalPortrait = CreatePng(Color.White, 2, 2);
        byte[] texture = CreatePackedTexture();
        CreateArchive(metPath, originalPortrait, CreateImportMap(), texture);

        PlayerStatsRecord player = PlayerStatsArchive.Load(metPath).Players.Single();
        PlayerPortraitArchive archive = PlayerPortraitArchive.Load(metPath);
        Assert.Equal(1, archive.PackedPortraitCount);
        Assert.True(archive.GetPortrait(player)!.HasPackedGameTexture);

        byte[] replacement = CreatePng(Color.Red, 2, 2);
        PlayerPortraitSaveResult result = archive.ReplaceWithBackup(player, replacement);

        Assert.Equal(1, result.PackedTextureCount);
        Assert.Equal(replacement, PlayerPortraitArchive.Load(metPath).GetPortrait(player)!.Data);
        using Bitmap savedTexture = LoadBitmap(ReadEntry(metPath, "data/menus/polaroids_0.png"));
        Color replaced = savedTexture.GetPixel(3, 4);
        Assert.Equal(128, replaced.A);
        Assert.Equal(Color.Red.R, replaced.R);
        Assert.Equal(Color.Red.G, replaced.G);
        Assert.Equal(Color.Red.B, replaced.B);
        Assert.Equal(Color.Green.ToArgb(), savedTexture.GetPixel(0, 0).ToArgb());
        Assert.True(METFileReader.ReadMETFile(metPath).ValidateStructure().IsValid);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory)) Directory.Delete(_tempDirectory, recursive: true);
    }

    private static byte[] CreateImportMap()
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(new byte[] { 0x49, 0x4d, 0x50, 0x1a });
        writer.Write(2);
        writer.Write(1);
        writer.Write(TextureId);
        WriteFixed(writer, "Polaroids_0", 60);
        writer.Write(1);
        WriteFixed(writer, "ABNE", 32);
        writer.Write(2);
        writer.Write(2);
        writer.Write(1);
        writer.Write(TextureId);
        writer.Write(3);
        writer.Write(4);
        writer.Write(2);
        writer.Write(2);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] CreatePackedTexture()
    {
        using Bitmap bitmap = new(16, 16, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(bitmap)) graphics.Clear(Color.Green);
        for (int y = 4; y < 6; y++)
        for (int x = 3; x < 5; x++)
            bitmap.SetPixel(x, y, Color.FromArgb(128, Color.Blue));
        return SavePng(bitmap);
    }

    private static byte[] CreatePng(Color color, int width, int height)
    {
        using Bitmap bitmap = new(width, height, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(bitmap)) graphics.Clear(color);
        return SavePng(bitmap);
    }

    private static byte[] SavePng(Bitmap bitmap)
    {
        using MemoryStream stream = new();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    private static Bitmap LoadBitmap(byte[] data)
    {
        using MemoryStream stream = new(data, writable: false);
        using Image image = Image.FromStream(stream);
        return new Bitmap(image);
    }

    private static byte[] ReadEntry(string metPath, string entryPath)
    {
        FileEntry entry = METFileReader.ReadMETFile(metPath).GetEntryByPath(entryPath)!;
        using FileStream stream = File.OpenRead(metPath);
        stream.Position = entry.Offset;
        byte[] data = new byte[entry.OriginalSize];
        stream.ReadExactly(data);
        return data;
    }

    private static void CreateArchive(string path, byte[] portrait, byte[] importMap, byte[] texture)
    {
        short[] values = Enumerable.Repeat((short)50, PlayerStatsRecord.BaseFieldCount).ToArray();
        byte[] stats;
        using (MemoryStream stream = new())
        using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
        {
            foreach (short value in values) writer.Write(value);
            writer.Write(Encoding.ASCII.GetBytes("Abner,Ace,Dubbleplay,"));
            writer.Flush();
            stats = stream.ToArray();
        }

        int totalLength = TextureOffset + texture.Length;
        using FileStream archive = new(path, FileMode.Create, FileAccess.ReadWrite);
        using BinaryWriter output = new(archive);
        output.Write(StatsOffset);
        output.Write(totalLength - StatsOffset);
        WriteEntry(output, StatsOffset, stats.Length, "data/kids/stats/abne_stats.dat");
        WriteEntry(output, PortraitOffset, portrait.Length, "data/polaroids/abne.png");
        WriteEntry(output, ImportOffset, importMap.Length, "data/menus/polaroids.imp");
        WriteEntry(output, TextureOffset, texture.Length, "data/menus/polaroids_0.png");
        output.Write(new byte[12]);
        archive.Position = StatsOffset;
        output.Write(stats);
        archive.Position = PortraitOffset;
        output.Write(portrait);
        archive.Position = ImportOffset;
        output.Write(importMap);
        archive.Position = TextureOffset;
        output.Write(texture);
    }

    private static void WriteEntry(BinaryWriter writer, int offset, int size, string path)
    {
        byte[] pathBytes = Encoding.ASCII.GetBytes(path);
        writer.Write(offset);
        writer.Write(size);
        writer.Write(pathBytes.Length);
        writer.Write(pathBytes);
    }

    private static void WriteFixed(BinaryWriter writer, string value, int length)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(value);
        writer.Write(bytes);
        writer.Write(new byte[length - bytes.Length]);
    }
}
