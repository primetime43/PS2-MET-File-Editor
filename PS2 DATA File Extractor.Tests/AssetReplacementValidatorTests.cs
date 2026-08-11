using PS2_DATA_File_Extractor.FileOperations;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text;

namespace PS2_DATA_File_Extractor.Tests;

public sealed class AssetReplacementValidatorTests : IDisposable
{
    private readonly string _tempDirectory =
        Path.Combine(Path.GetTempPath(), $"asset-validation-tests-{Guid.NewGuid():N}");

    [Fact]
    public void PngTextureMustBeValidAndKeepOriginalDimensions()
    {
        byte[] original = CreateImage(ImageFormat.Png, 64, 32, PixelFormat.Format32bppArgb);
        byte[] compatible = CreateImage(ImageFormat.Png, 64, 32, PixelFormat.Format32bppArgb);
        byte[] wrongSize = CreateImage(ImageFormat.Png, 32, 32, PixelFormat.Format32bppArgb);

        AssetReplacementValidation valid =
            AssetReplacementValidator.Validate("data/test.png", original, compatible);
        AssetReplacementValidation invalid =
            AssetReplacementValidator.Validate("data/test.png", original, wrongSize);

        Assert.True(valid.IsValid);
        Assert.Equal(AssetReplacementKind.PngTexture, valid.Kind);
        Assert.Contains("64 x 32", valid.Description);
        Assert.False(invalid.IsValid);
        Assert.Contains(invalid.Errors, error => error.Contains("dimensions", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BmpTextureMustKeepDimensionsAndReportsPixelDepthChanges()
    {
        byte[] original = CreateImage(ImageFormat.Bmp, 32, 16, PixelFormat.Format24bppRgb);
        byte[] changedDepth = CreateImage(ImageFormat.Bmp, 32, 16, PixelFormat.Format32bppArgb);
        byte[] wrongSize = CreateImage(ImageFormat.Bmp, 16, 16, PixelFormat.Format24bppRgb);

        AssetReplacementValidation warning =
            AssetReplacementValidator.Validate("data/test.bmp", original, changedDepth);
        AssetReplacementValidation invalid =
            AssetReplacementValidator.Validate("data/test.bmp", original, wrongSize);

        Assert.True(warning.IsValid);
        Assert.Contains(warning.Warnings, item => item.Contains("pixel depth", StringComparison.OrdinalIgnoreCase));
        Assert.False(invalid.IsValid);
        Assert.Contains(invalid.Errors, error => error.Contains("dimensions", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BmpTextureAcceptsRetailRle8Compression()
    {
        byte[] rle8 = CreateBmpHeader(32, 32, 8, 1);

        AssetReplacementValidation result =
            AssetReplacementValidator.Validate("data/fields/test.bmp", rle8, rle8);

        Assert.True(result.IsValid);
        Assert.Equal(AssetReplacementKind.BmpTexture, result.Kind);
    }

    [Fact]
    public void VagAudioChecksHeaderFramesAndReportsSampleRateChanges()
    {
        byte[] original = CreateVag(22050, 12);
        byte[] differentRate = CreateVag(44100, 24);
        byte[] invalid = CreateVag(22050, 12);
        invalid[15] = 0xC1;

        AssetReplacementValidation warning =
            AssetReplacementValidator.Validate("data/audio/test.vag", original, differentRate);
        AssetReplacementValidation failed =
            AssetReplacementValidator.Validate("data/audio/test.vag", original, invalid);

        Assert.True(warning.IsValid);
        Assert.Equal(AssetReplacementKind.VagAudio, warning.Kind);
        Assert.Contains(warning.Warnings, item => item.Contains("sample rate", StringComparison.OrdinalIgnoreCase));
        Assert.False(failed.IsValid);
        Assert.Contains(failed.Errors, item => item.Contains("outside the file", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PssVideoChecksProgramStreamAndMatchesOriginalDimensions()
    {
        byte[] original = CreatePss(256, 256, hasAudio: true);
        byte[] noAudio = CreatePss(256, 256, hasAudio: false);
        byte[] wrongSize = CreatePss(640, 448, hasAudio: true);

        AssetReplacementValidation warning =
            AssetReplacementValidator.Validate("data/video/test.pss", original, noAudio);
        AssetReplacementValidation invalid =
            AssetReplacementValidator.Validate("data/video/test.pss", original, wrongSize);

        Assert.True(warning.IsValid);
        Assert.Equal(AssetReplacementKind.PssVideo, warning.Kind);
        Assert.Contains(warning.Warnings, item => item.Contains("no private audio", StringComparison.OrdinalIgnoreCase));
        Assert.False(invalid.IsValid);
        Assert.Contains(invalid.Errors, item => item.Contains("dimensions", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("bad.png")]
    [InlineData("bad.bmp")]
    [InlineData("bad.vag")]
    [InlineData("bad.pss")]
    public void RecognizedAssetsRejectUnrelatedBytes(string path)
    {
        byte[] original = Path.GetExtension(path) switch
        {
            ".png" => CreateImage(ImageFormat.Png, 8, 8, PixelFormat.Format32bppArgb),
            ".bmp" => CreateImage(ImageFormat.Bmp, 8, 8, PixelFormat.Format24bppRgb),
            ".vag" => CreateVag(22050, 2),
            ".pss" => CreatePss(256, 256, hasAudio: true),
            _ => throw new InvalidOperationException()
        };

        AssetReplacementValidation result =
            AssetReplacementValidator.Validate(path, original, new byte[128]);

        Assert.True(result.IsAsset);
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void UnknownFileTypesRemainByteForByteImports()
    {
        AssetReplacementValidation result =
            AssetReplacementValidator.Validate("data/example.dat", new byte[] { 1 }, new byte[] { 2 });

        Assert.False(result.IsAsset);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void BatchEditorRejectsInvalidTextureBeforeCreatingBackupOrChangingArchive()
    {
        Directory.CreateDirectory(_tempDirectory);
        string metPath = Path.Combine(_tempDirectory, "DATA.MET");
        byte[] texture = CreateImage(ImageFormat.Png, 8, 8, PixelFormat.Format32bppArgb);
        CreateArchive(metPath, "data/test.png", texture);
        byte[] before = File.ReadAllBytes(metPath);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            METArchiveBatchEditor.SaveWithBackup(
                metPath,
                new Dictionary<string, byte[]> { ["data/test.png"] = new byte[128] },
                "test"));

        Assert.Contains("replacement validation", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, File.ReadAllBytes(metPath));
        Assert.Empty(Directory.GetFiles(_tempDirectory, "*.backup_*"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, recursive: true);
    }

    private static byte[] CreateImage(
        ImageFormat format,
        int width,
        int height,
        PixelFormat pixelFormat)
    {
        using Bitmap bitmap = new(width, height, pixelFormat);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.CornflowerBlue);
        using MemoryStream stream = new();
        bitmap.Save(stream, format);
        return stream.ToArray();
    }

    private static byte[] CreateBmpHeader(int width, int height, ushort bitsPerPixel, uint compression)
    {
        byte[] data = new byte[55];
        data[0] = (byte)'B';
        data[1] = (byte)'M';
        WriteLittleEndian(data, 2, (uint)data.Length);
        WriteLittleEndian(data, 10, 54);
        WriteLittleEndian(data, 14, 40);
        WriteLittleEndian(data, 18, (uint)width);
        WriteLittleEndian(data, 22, (uint)height);
        data[26] = 1;
        data[28] = (byte)bitsPerPixel;
        data[29] = (byte)(bitsPerPixel >> 8);
        WriteLittleEndian(data, 30, compression);
        return data;
    }

    private static byte[] CreateVag(int sampleRate, int frameCount)
    {
        byte[] result = new byte[48 + frameCount * 16];
        Encoding.ASCII.GetBytes("VAGp").CopyTo(result, 0);
        WriteBigEndian(result, 4, 0x20);
        WriteBigEndian(result, 12, (uint)(frameCount * 16));
        WriteBigEndian(result, 16, (uint)sampleRate);
        Encoding.ASCII.GetBytes("validation-test").CopyTo(result, 32);
        return result;
    }

    private static byte[] CreatePss(int width, int height, bool hasAudio)
    {
        byte[] data = new byte[2048];
        WriteStartCode(data, 0, 0xba);
        WriteStartCode(data, 8, 0xbb);
        WriteStartCode(data, 12, 0xe0);
        if (hasAudio) WriteStartCode(data, 16, 0xbd);
        WriteStartCode(data, 32, 0xb3);
        data[36] = (byte)(width >> 4);
        data[37] = (byte)(((width & 0x0f) << 4) | (height >> 8));
        data[38] = (byte)height;
        data[39] = 0x04;
        WriteStartCode(data, 48, 0x00);
        return data;
    }

    private static void WriteStartCode(byte[] data, int offset, byte code)
    {
        data[offset] = 0x00;
        data[offset + 1] = 0x00;
        data[offset + 2] = 0x01;
        data[offset + 3] = code;
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

    private static void CreateArchive(string path, string entryPath, byte[] data)
    {
        const int dataOffset = 2048;
        using FileStream stream = new(path, FileMode.Create, FileAccess.ReadWrite);
        using BinaryWriter writer = new(stream);
        writer.Write(dataOffset);
        writer.Write(data.Length);
        byte[] pathBytes = Encoding.ASCII.GetBytes(entryPath);
        writer.Write(dataOffset);
        writer.Write(data.Length);
        writer.Write(pathBytes.Length);
        writer.Write(pathBytes);
        writer.Write(new byte[12]);
        stream.Position = dataOffset;
        writer.Write(data);
    }
}
