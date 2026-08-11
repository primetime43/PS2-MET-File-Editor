using System.Text;
using PS2_DATA_File_Extractor.Models;

namespace PS2_DATA_File_Extractor.FileOperations;

public enum Ps2AudioKind
{
    Vag,
    MibMih
}

public sealed record Ps2AudioInfo(
    Ps2AudioKind Kind,
    int Channels,
    int SampleRate,
    int SamplesPerChannel,
    double DurationSeconds,
    int CompressedDataSize,
    string DataPath,
    string? HeaderPath = null,
    int InterleaveBlockSize = 0,
    int BlockCount = 0,
    int LastBlockSize = 0,
    string? StreamName = null);

public static class Ps2AudioArchive
{
    private static readonly int[] PositiveCoefficients = { 0, 60, 115, 98, 122 };
    private static readonly int[] NegativeCoefficients = { 0, 0, -52, -55, -60 };

    public static bool IsSupported(string path)
    {
        string extension = Path.GetExtension(path);
        return extension.Equals(".vag", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".mib", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".mih", StringComparison.OrdinalIgnoreCase);
    }

    public static Ps2AudioInfo Inspect(
        string metPath,
        FileEntry selectedEntry,
        METFileStructure structure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(metPath);
        ArgumentNullException.ThrowIfNull(selectedEntry);
        ArgumentNullException.ThrowIfNull(structure);

        string extension = Path.GetExtension(selectedEntry.Path);
        if (extension.Equals(".vag", StringComparison.OrdinalIgnoreCase))
            return ParseVag(selectedEntry, ReadEntry(metPath, selectedEntry)).Info;

        if (extension.Equals(".mib", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".mih", StringComparison.OrdinalIgnoreCase))
            return LoadMibMih(metPath, selectedEntry, structure).Info;

        throw new InvalidDataException($"{selectedEntry.Path} is not a supported PS2 audio asset.");
    }

    public static byte[] DecodeToWave(
        string metPath,
        FileEntry selectedEntry,
        METFileStructure structure)
    {
        string extension = Path.GetExtension(selectedEntry.Path);
        if (extension.Equals(".vag", StringComparison.OrdinalIgnoreCase))
        {
            VagAsset vag = ParseVag(selectedEntry, ReadEntry(metPath, selectedEntry));
            List<short> samples = new(vag.Info.SamplesPerChannel);
            int vagHistory1 = 0;
            int vagHistory2 = 0;
            for (int offset = 48; offset < 48 + vag.DataSize; offset += 16)
                DecodeFrame(vag.Data.AsSpan(offset, 16), samples, ref vagHistory1, ref vagHistory2);
            return CreateWave(new[] { samples }, vag.Info.SampleRate);
        }

        MibMihAsset asset = LoadMibMih(metPath, selectedEntry, structure);
        List<short>[] channels = Enumerable.Range(0, asset.Info.Channels)
            .Select(_ => new List<short>(asset.Info.SamplesPerChannel))
            .ToArray();
        int[] history1 = new int[channels.Length];
        int[] history2 = new int[channels.Length];

        for (int block = 0; block < asset.FrameCount; block++)
        {
            int usableBytes = block == asset.FrameCount - 1 ? asset.LastBlockSize : asset.FrameSize;
            for (int channel = 0; channel < channels.Length; channel++)
            {
                int blockOffset = checked((block * channels.Length + channel) * asset.FrameSize);
                for (int frameOffset = 0; frameOffset < usableBytes; frameOffset += 16)
                {
                    DecodeFrame(
                        asset.MibData.AsSpan(blockOffset + frameOffset, 16),
                        channels[channel],
                        ref history1[channel],
                        ref history2[channel]);
                }
            }
        }

        return CreateWave(channels, asset.Info.SampleRate);
    }

    public static void ExportRawPair(
        string metPath,
        FileEntry selectedEntry,
        METFileStructure structure,
        string mibDestinationPath)
    {
        MibMihAsset asset = LoadMibMih(metPath, selectedEntry, structure);
        string basePath = Path.Combine(
            Path.GetDirectoryName(mibDestinationPath) ?? string.Empty,
            Path.GetFileNameWithoutExtension(mibDestinationPath));
        File.WriteAllBytes(basePath + ".mib", asset.MibData);
        File.WriteAllBytes(basePath + ".mih", asset.MihData);
    }

    public static string FormatDescription(Ps2AudioInfo info)
    {
        StringBuilder text = new();
        text.AppendLine(info.Kind == Ps2AudioKind.MibMih
            ? "[PlayStation 2 streamed audio]"
            : "[PlayStation 2 VAG audio]");
        text.AppendLine();
        text.AppendLine($"Format: {(info.Kind == Ps2AudioKind.MibMih ? "Sony MultiStream MIH + MIB" : "VAGp")} (PSX ADPCM)");
        text.AppendLine($"Channels: {info.Channels}");
        text.AppendLine($"Sample rate: {info.SampleRate:N0} Hz");
        text.AppendLine($"Samples per channel: {info.SamplesPerChannel:N0}");
        text.AppendLine($"Duration: {TimeSpan.FromSeconds(info.DurationSeconds):mm\\:ss\\.fff}");
        text.AppendLine($"Compressed audio: {info.CompressedDataSize:N0} bytes");
        if (!string.IsNullOrWhiteSpace(info.StreamName))
            text.AppendLine($"Stream name: {info.StreamName}");
        if (info.Kind == Ps2AudioKind.MibMih)
        {
            text.AppendLine($"Interleave block: {info.InterleaveBlockSize:N0} bytes per channel");
            text.AppendLine($"Blocks: {info.BlockCount:N0}");
            text.AppendLine($"Last block used: {info.LastBlockSize:N0} bytes per channel");
            text.AppendLine($"Header entry: {info.HeaderPath}");
            text.AppendLine($"Audio entry: {info.DataPath}");
        }
        else
        {
            text.AppendLine($"Archive entry: {info.DataPath}");
        }

        text.AppendLine();
        text.Append("Export Selected can decode this asset to a standard PCM WAV file.");
        if (info.Kind == Ps2AudioKind.MibMih)
            text.Append(" It can also export the original MIH/MIB pair.");
        return text.ToString();
    }

    private static MibMihAsset LoadMibMih(
        string metPath,
        FileEntry selectedEntry,
        METFileStructure structure)
    {
        string basePath = NormalizePath(Path.ChangeExtension(selectedEntry.Path, null) ?? selectedEntry.Path);
        FileEntry mihEntry = FindEntry(structure, basePath + ".mih");
        FileEntry mibEntry = FindEntry(structure, basePath + ".mib");
        byte[] mih = ReadEntry(metPath, mihEntry);
        byte[] mib = ReadEntry(metPath, mibEntry);
        if (mih.Length < 0x18 || ReadUInt32LittleEndian(mih, 0) != 0x40)
            throw new InvalidDataException($"{mihEntry.Path} does not contain a standard 64-byte MIH header.");

        uint packedLastBlock = ReadUInt32LittleEndian(mih, 0x04);
        int lastBlockSize = checked((int)(packedLastBlock >> 8));
        int channels = checked((int)ReadUInt32LittleEndian(mih, 0x08));
        int sampleRate = checked((int)ReadUInt32LittleEndian(mih, 0x0c));
        int frameSize = checked((int)ReadUInt32LittleEndian(mih, 0x10));
        int frameCount = checked((int)ReadUInt32LittleEndian(mih, 0x14));
        if (channels is < 1 or > 8)
            throw new InvalidDataException($"The MIH channel count ({channels}) is unsupported.");
        if (sampleRate is < 8000 or > 192000)
            throw new InvalidDataException($"The MIH sample rate ({sampleRate}) is invalid.");
        if (frameSize <= 0 || frameSize % 16 != 0)
            throw new InvalidDataException("The MIH interleave block size is not aligned to PSX ADPCM frames.");
        if (frameCount <= 0)
            throw new InvalidDataException("The MIH block count is zero.");
        if (lastBlockSize == 0) lastBlockSize = frameSize;
        if (lastBlockSize > frameSize || lastBlockSize % 16 != 0)
            throw new InvalidDataException("The MIH final block size is invalid.");

        long physicalSize = checked((long)frameSize * frameCount * channels);
        if (physicalSize > mib.Length)
            throw new InvalidDataException(
                $"The MIB is truncated: the MIH requires {physicalSize:N0} bytes but only {mib.Length:N0} are stored.");

        int usablePerChannel = checked((frameCount - 1) * frameSize + lastBlockSize);
        int samplesPerChannel = checked(usablePerChannel / 16 * 28);
        int compressedSize = checked(usablePerChannel * channels);
        Ps2AudioInfo info = new(
            Ps2AudioKind.MibMih,
            channels,
            sampleRate,
            samplesPerChannel,
            (double)samplesPerChannel / sampleRate,
            compressedSize,
            mibEntry.Path,
            mihEntry.Path,
            frameSize,
            frameCount,
            lastBlockSize);
        return new MibMihAsset(info, mih, mib, frameSize, frameCount, lastBlockSize);
    }

    private static VagAsset ParseVag(FileEntry entry, byte[] data)
    {
        if (data.Length < 48 || !data.AsSpan(0, 4).SequenceEqual("VAGp"u8))
            throw new InvalidDataException($"{entry.Path} does not contain a standard VAGp header.");

        int dataSize = checked((int)ReadUInt32BigEndian(data, 0x0c));
        int sampleRate = checked((int)ReadUInt32BigEndian(data, 0x10));
        if (dataSize <= 0 || dataSize > data.Length - 48 || dataSize % 16 != 0)
            throw new InvalidDataException("The VAG audio size is invalid or not aligned to 16-byte ADPCM frames.");
        if (sampleRate is < 8000 or > 192000)
            throw new InvalidDataException($"The VAG sample rate ({sampleRate}) is invalid.");

        int samples = checked(dataSize / 16 * 28);
        int nameEnd = Array.IndexOf(data, (byte)0, 0x20, 16);
        if (nameEnd < 0) nameEnd = 0x30;
        string name = Encoding.ASCII.GetString(data, 0x20, nameEnd - 0x20);
        Ps2AudioInfo info = new(
            Ps2AudioKind.Vag,
            1,
            sampleRate,
            samples,
            (double)samples / sampleRate,
            dataSize,
            entry.Path,
            StreamName: name);
        return new VagAsset(info, data, dataSize);
    }

    private static void DecodeFrame(
        ReadOnlySpan<byte> frame,
        ICollection<short> output,
        ref int history1,
        ref int history2)
    {
        int predictor = frame[0] >> 4;
        int shift = frame[0] & 0x0f;
        if (predictor > 4 || shift > 12)
            throw new InvalidDataException(
                $"The PSX ADPCM stream contains invalid predictor/shift values ({predictor}/{shift}).");

        int positive = PositiveCoefficients[predictor];
        int negative = NegativeCoefficients[predictor];
        for (int index = 2; index < 16; index++)
        {
            int packed = frame[index];
            DecodeNibble(packed & 0x0f, shift, positive, negative, output, ref history1, ref history2);
            DecodeNibble(packed >> 4, shift, positive, negative, output, ref history1, ref history2);
        }
    }

    private static void DecodeNibble(
        int nibble,
        int shift,
        int positive,
        int negative,
        ICollection<short> output,
        ref int history1,
        ref int history2)
    {
        int signed = nibble >= 8 ? nibble - 16 : nibble;
        int sample = (signed << 12) >> shift;
        sample += (history1 * positive + history2 * negative + 32) >> 6;
        sample = Math.Clamp(sample, short.MinValue, short.MaxValue);
        history2 = history1;
        history1 = sample;
        output.Add((short)sample);
    }

    private static byte[] CreateWave(IReadOnlyList<List<short>> channels, int sampleRate)
    {
        int sampleCount = channels.Min(channel => channel.Count);
        int channelCount = channels.Count;
        int dataSize = checked(sampleCount * channelCount * sizeof(short));
        using MemoryStream stream = new(44 + dataSize);
        using BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write("RIFF"u8);
        writer.Write(36 + dataSize);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)channelCount);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channelCount * sizeof(short));
        writer.Write((short)(channelCount * sizeof(short)));
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataSize);
        for (int sample = 0; sample < sampleCount; sample++)
        {
            for (int channel = 0; channel < channelCount; channel++)
                writer.Write(channels[channel][sample]);
        }
        writer.Flush();
        return stream.ToArray();
    }

    private static FileEntry FindEntry(METFileStructure structure, string normalizedPath) =>
        structure.AllEntries.FirstOrDefault(entry =>
            NormalizePath(entry.Path).Equals(normalizedPath, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidDataException($"The companion archive entry '{normalizedPath}' was not found.");

    private static byte[] ReadEntry(string metPath, FileEntry entry)
    {
        using FileStream stream = new(metPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        stream.Position = entry.Offset;
        byte[] data = new byte[entry.OriginalSize];
        stream.ReadExactly(data);
        return data;
    }

    private static uint ReadUInt32LittleEndian(byte[] data, int offset) =>
        data[offset] | ((uint)data[offset + 1] << 8) |
        ((uint)data[offset + 2] << 16) | ((uint)data[offset + 3] << 24);

    private static uint ReadUInt32BigEndian(byte[] data, int offset) =>
        ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) |
        ((uint)data[offset + 2] << 8) | data[offset + 3];

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    private sealed record MibMihAsset(
        Ps2AudioInfo Info,
        byte[] MihData,
        byte[] MibData,
        int FrameSize,
        int FrameCount,
        int LastBlockSize);

    private sealed record VagAsset(Ps2AudioInfo Info, byte[] Data, int DataSize);
}
