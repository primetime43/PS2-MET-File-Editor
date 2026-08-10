using System.Buffers.Binary;

namespace PS2_DATA_File_Extractor.FileOperations;

/// <summary>
/// Reads and writes the Backyard Baseball PS2 memory-card file named "Settings".
/// </summary>
public sealed class GameSettingsFile
{
    public const int UnlockMaskOffset = 0x24;
    public const uint SimpleUnlockMask = 0x0FFF;
    public const uint AquadomeProgressMask = 0xF000;
    public const uint KnownUnlockMask = SimpleUnlockMask | AquadomeProgressMask;

    private const int MinimumDataLength = 0x28;
    private readonly byte[] _fileData;

    private GameSettingsFile(byte[] fileData, int dataLength, uint unlockMask)
    {
        _fileData = fileData;
        DataLength = dataLength;
        UnlockMask = unlockMask;
    }

    public int DataLength { get; }

    public uint UnlockMask { get; set; }

    public static GameSettingsFile Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Parse(File.ReadAllBytes(path));
    }

    public static GameSettingsFile Parse(byte[] fileData)
    {
        ArgumentNullException.ThrowIfNull(fileData);
        if (fileData.Length < MinimumDataLength + sizeof(uint))
        {
            throw new InvalidDataException("The Settings file is too small to contain the options block and CRC-32.");
        }

        uint declaredLength = BinaryPrimitives.ReadUInt32LittleEndian(fileData.AsSpan(0, sizeof(uint)));
        if (declaredLength < MinimumDataLength || declaredLength > int.MaxValue)
        {
            throw new InvalidDataException($"The Settings file declares an invalid data length: 0x{declaredLength:X}.");
        }

        int dataLength = (int)declaredLength;
        if (fileData.Length != dataLength + sizeof(uint))
        {
            throw new InvalidDataException(
                $"The Settings file length is inconsistent: expected {dataLength + sizeof(uint)} bytes, found {fileData.Length}.");
        }

        uint storedCrc = BinaryPrimitives.ReadUInt32LittleEndian(fileData.AsSpan(dataLength, sizeof(uint)));
        uint computedCrc = ComputeCrc32(fileData.AsSpan(0, dataLength));
        if (storedCrc != computedCrc)
        {
            throw new InvalidDataException(
                $"The Settings file CRC-32 is invalid: stored 0x{storedCrc:X8}, computed 0x{computedCrc:X8}.");
        }

        uint unlockMask = BinaryPrimitives.ReadUInt32LittleEndian(
            fileData.AsSpan(UnlockMaskOffset, sizeof(uint)));
        return new GameSettingsFile((byte[])fileData.Clone(), dataLength, unlockMask);
    }

    public string SaveWithBackup(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        string backupPath = $"{path}.backup_{timestamp}";
        File.Copy(path, backupPath, overwrite: false);

        BinaryPrimitives.WriteUInt32LittleEndian(
            _fileData.AsSpan(UnlockMaskOffset, sizeof(uint)), UnlockMask);
        uint crc = ComputeCrc32(_fileData.AsSpan(0, DataLength));
        BinaryPrimitives.WriteUInt32LittleEndian(_fileData.AsSpan(DataLength, sizeof(uint)), crc);

        try
        {
            File.WriteAllBytes(path, _fileData);
        }
        catch
        {
            File.Copy(backupPath, path, overwrite: true);
            throw;
        }

        return backupPath;
    }

    public static uint ComputeCrc32(ReadOnlySpan<byte> data, uint seed = 0)
    {
        uint crc = ~seed;
        foreach (byte value in data)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                uint polynomial = (crc & 1) == 0 ? 0 : 0xEDB88320u;
                crc = (crc >> 1) ^ polynomial;
            }
        }

        return ~crc;
    }
}
