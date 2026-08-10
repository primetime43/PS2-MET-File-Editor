using System.Buffers.Binary;
using System.Text;
using PS2_DATA_File_Extractor.FileOperations;
using PS2_DATA_File_Extractor.Models;

namespace PS2_DATA_File_Extractor.Tests;

public sealed class GameSettingsFileTests : IDisposable
{
    private readonly string _tempDirectory =
        Path.Combine(Path.GetTempPath(), $"ps2-settings-tests-{Guid.NewGuid():N}");

    [Fact]
    public void ComputeCrc32MatchesStandardCheckVector()
    {
        Assert.Equal(0xCBF43926u, GameSettingsFile.ComputeCrc32(Encoding.ASCII.GetBytes("123456789")));
    }

    [Fact]
    public void ParseReadsUnlockMaskAndRejectsDamagedCrc()
    {
        byte[] fileData = CreateSettingsFile(0x0000A55Au);

        GameSettingsFile settings = GameSettingsFile.Parse(fileData);

        Assert.Equal(0x0000A55Au, settings.UnlockMask);

        fileData[0x10] ^= 0x80;
        Assert.Throws<InvalidDataException>(() => GameSettingsFile.Parse(fileData));
    }

    [Fact]
    public void SaveUpdatesMaskAndCrcAndPreservesBackup()
    {
        Directory.CreateDirectory(_tempDirectory);
        string path = Path.Combine(_tempDirectory, "Settings");
        byte[] original = CreateSettingsFile(0x00000001u);
        File.WriteAllBytes(path, original);

        GameSettingsFile settings = GameSettingsFile.Load(path);
        settings.UnlockMask = GameSettingsFile.KnownUnlockMask;
        string backupPath = settings.SaveWithBackup(path);

        Assert.True(File.Exists(backupPath));
        Assert.Equal(original, File.ReadAllBytes(backupPath));
        Assert.Equal(GameSettingsFile.KnownUnlockMask, GameSettingsFile.Load(path).UnlockMask);
    }

    [Fact]
    public void UnlockTableCoversEveryKnownBitExactlyOnce()
    {
        uint combined = 0;
        foreach (UnlockableContent item in UnlockableContent.Items)
        {
            Assert.Equal(0u, combined & item.Mask);
            combined |= item.Mask;
        }

        Assert.Equal(GameSettingsFile.KnownUnlockMask, combined);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private static byte[] CreateSettingsFile(uint unlockMask)
    {
        const int dataLength = 0x40;
        byte[] fileData = new byte[dataLength + sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(fileData.AsSpan(0, sizeof(uint)), dataLength);
        BinaryPrimitives.WriteUInt32LittleEndian(fileData.AsSpan(4, sizeof(uint)), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            fileData.AsSpan(GameSettingsFile.UnlockMaskOffset, sizeof(uint)), unlockMask);

        for (int index = 8; index < dataLength; index++)
        {
            if (index < GameSettingsFile.UnlockMaskOffset ||
                index >= GameSettingsFile.UnlockMaskOffset + sizeof(uint))
            {
                fileData[index] = (byte)(index * 13);
            }
        }

        uint crc = GameSettingsFile.ComputeCrc32(fileData.AsSpan(0, dataLength));
        BinaryPrimitives.WriteUInt32LittleEndian(fileData.AsSpan(dataLength, sizeof(uint)), crc);
        return fileData;
    }
}
