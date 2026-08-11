using System.Text;

namespace PS2_DATA_File_Extractor.FileOperations;

public enum AssetReplacementKind
{
    None,
    PngTexture,
    BmpTexture,
    VagAudio,
    PssVideo
}

public sealed record AssetReplacementValidation(
    AssetReplacementKind Kind,
    string Description,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings)
{
    public bool IsAsset => Kind != AssetReplacementKind.None;
    public bool IsValid => Errors.Count == 0;

    public string FormatErrors() => string.Join(Environment.NewLine, Errors.Select(error => $"• {error}"));
    public string FormatWarnings() => string.Join(Environment.NewLine, Warnings.Select(warning => $"• {warning}"));
}

public static class AssetReplacementValidator
{
    public static AssetReplacementValidation Validate(
        string targetPath,
        byte[] originalData,
        byte[] replacementData)
    {
        ArgumentNullException.ThrowIfNull(originalData);
        ArgumentNullException.ThrowIfNull(replacementData);
        return Validate(targetPath, originalData.AsSpan(), replacementData.AsSpan());
    }

    public static AssetReplacementValidation Validate(
        string targetPath,
        ReadOnlySpan<byte> originalData,
        ReadOnlySpan<byte> replacementData)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        string extension = Path.GetExtension(targetPath).ToLowerInvariant();
        return extension switch
        {
            ".png" => ValidatePng(originalData, replacementData),
            ".bmp" => ValidateBmp(originalData, replacementData),
            ".vag" => ValidateVag(originalData, replacementData),
            ".pss" => ValidatePss(originalData, replacementData),
            _ => new AssetReplacementValidation(
                AssetReplacementKind.None,
                "No format-specific validation is defined for this file type.",
                Array.Empty<string>(),
                Array.Empty<string>())
        };
    }

    private static AssetReplacementValidation ValidatePng(
        ReadOnlySpan<byte> originalData,
        ReadOnlySpan<byte> replacementData)
    {
        List<string> errors = new();
        List<string> warnings = new();
        PngInfo? replacement = TryReadPng(replacementData, errors);
        PngInfo? original = TryReadPng(originalData, new List<string>());

        if (replacement != null && original != null)
        {
            if (replacement.Width != original.Width || replacement.Height != original.Height)
                errors.Add(
                    $"Texture dimensions must remain {original.Width} x {original.Height}; " +
                    $"the replacement is {replacement.Width} x {replacement.Height}.");

            if (replacement.BitDepth != original.BitDepth || replacement.ColorType != original.ColorType)
                warnings.Add(
                    $"The PNG pixel format changed from {DescribePngFormat(original)} " +
                    $"to {DescribePngFormat(replacement)}.");
        }

        string description = replacement == null
            ? "PNG texture"
            : $"PNG texture — {replacement.Width} x {replacement.Height}, {DescribePngFormat(replacement)}";
        return new AssetReplacementValidation(
            AssetReplacementKind.PngTexture, description, errors, warnings);
    }

    private static PngInfo? TryReadPng(ReadOnlySpan<byte> data, ICollection<string> errors)
    {
        ReadOnlySpan<byte> signature = new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a };
        if (data.Length < 33 || !data[..8].SequenceEqual(signature))
        {
            errors.Add("The replacement does not have a valid PNG signature.");
            return null;
        }

        int position = 8;
        bool sawHeader = false;
        bool sawImageData = false;
        bool sawEnd = false;
        PngInfo? info = null;
        while (position <= data.Length - 12)
        {
            uint chunkLength = ReadBigEndianUInt32(data.Slice(position, 4));
            if (chunkLength > int.MaxValue || position > data.Length - 12 - (int)chunkLength)
            {
                errors.Add("The PNG contains a truncated or out-of-range chunk.");
                return null;
            }

            ReadOnlySpan<byte> type = data.Slice(position + 4, 4);
            ReadOnlySpan<byte> chunk = data.Slice(position + 8, (int)chunkLength);
            if (type.SequenceEqual("IHDR"u8))
            {
                if (sawHeader || position != 8 || chunkLength != 13)
                {
                    errors.Add("The PNG has an invalid IHDR chunk.");
                    return null;
                }

                uint rawWidth = ReadBigEndianUInt32(chunk[..4]);
                uint rawHeight = ReadBigEndianUInt32(chunk.Slice(4, 4));
                int width = rawWidth <= int.MaxValue ? (int)rawWidth : 0;
                int height = rawHeight <= int.MaxValue ? (int)rawHeight : 0;
                byte bitDepth = chunk[8];
                byte colorType = chunk[9];
                if (width <= 0 || height <= 0 || width > 8192 || height > 8192)
                    errors.Add("PNG dimensions must be between 1 and 8192 pixels.");
                if (!IsValidPngPixelFormat(bitDepth, colorType))
                    errors.Add($"The PNG bit depth/color type combination ({bitDepth}/{colorType}) is invalid.");
                if (chunk[10] != 0 || chunk[11] != 0 || chunk[12] > 1)
                    errors.Add("The PNG uses unsupported compression, filtering, or interlacing values.");

                info = new PngInfo(width, height, bitDepth, colorType);
                sawHeader = true;
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                sawImageData = true;
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                sawEnd = chunkLength == 0;
                break;
            }

            position += checked(12 + (int)chunkLength);
        }

        if (!sawHeader) errors.Add("The PNG is missing its IHDR chunk.");
        if (!sawImageData) errors.Add("The PNG contains no image data.");
        if (!sawEnd) errors.Add("The PNG is missing a valid IEND chunk.");
        return errors.Count == 0 ? info : null;
    }

    private static bool IsValidPngPixelFormat(byte bitDepth, byte colorType) => colorType switch
    {
        0 => bitDepth is 1 or 2 or 4 or 8 or 16,
        2 => bitDepth is 8 or 16,
        3 => bitDepth is 1 or 2 or 4 or 8,
        4 => bitDepth is 8 or 16,
        6 => bitDepth is 8 or 16,
        _ => false
    };

    private static string DescribePngFormat(PngInfo info)
    {
        string color = info.ColorType switch
        {
            0 => "grayscale",
            2 => "RGB",
            3 => "indexed",
            4 => "grayscale + alpha",
            6 => "RGBA",
            _ => $"color type {info.ColorType}"
        };
        return $"{info.BitDepth}-bit {color}";
    }

    private static AssetReplacementValidation ValidateBmp(
        ReadOnlySpan<byte> originalData,
        ReadOnlySpan<byte> replacementData)
    {
        List<string> errors = new();
        List<string> warnings = new();
        BmpInfo? replacement = TryReadBmp(replacementData, errors);
        BmpInfo? original = TryReadBmp(originalData, new List<string>());

        if (replacement != null && original != null)
        {
            if (replacement.Width != original.Width || replacement.Height != original.Height)
                errors.Add(
                    $"Texture dimensions must remain {original.Width} x {original.Height}; " +
                    $"the replacement is {replacement.Width} x {replacement.Height}.");

            if (replacement.BitsPerPixel != original.BitsPerPixel)
                warnings.Add(
                    $"The BMP pixel depth changed from {original.BitsPerPixel}-bit " +
                    $"to {replacement.BitsPerPixel}-bit.");
        }

        string description = replacement == null
            ? "BMP texture"
            : $"BMP texture — {replacement.Width} x {replacement.Height}, {replacement.BitsPerPixel}-bit";
        return new AssetReplacementValidation(
            AssetReplacementKind.BmpTexture, description, errors, warnings);
    }

    private static BmpInfo? TryReadBmp(ReadOnlySpan<byte> data, ICollection<string> errors)
    {
        if (data.Length < 54 || data[0] != (byte)'B' || data[1] != (byte)'M')
        {
            errors.Add("The replacement does not have a valid Windows BMP header.");
            return null;
        }

        uint declaredSize = ReadLittleEndianUInt32(data.Slice(2, 4));
        uint pixelOffset = ReadLittleEndianUInt32(data.Slice(10, 4));
        uint dibSize = ReadLittleEndianUInt32(data.Slice(14, 4));
        if (dibSize < 40 || dibSize > data.Length - 14)
            errors.Add("The BMP has an unsupported or truncated DIB header.");
        if (declaredSize > data.Length || declaredSize < pixelOffset)
            errors.Add("The BMP file-size or pixel-data offset is invalid.");
        if (pixelOffset >= data.Length)
            errors.Add("The BMP pixel data lies outside the file.");

        int width = ReadLittleEndianInt32(data.Slice(18, 4));
        int rawHeight = ReadLittleEndianInt32(data.Slice(22, 4));
        int height = rawHeight == int.MinValue ? 0 : Math.Abs(rawHeight);
        ushort planes = ReadLittleEndianUInt16(data.Slice(26, 2));
        ushort bitsPerPixel = ReadLittleEndianUInt16(data.Slice(28, 2));
        uint compression = ReadLittleEndianUInt32(data.Slice(30, 4));
        if (width <= 0 || height <= 0 || width > 8192 || height > 8192)
            errors.Add("BMP dimensions must be between 1 and 8192 pixels.");
        if (planes != 1)
            errors.Add("The BMP must contain exactly one image plane.");
        if (bitsPerPixel is not (4 or 8 or 16 or 24 or 32))
            errors.Add($"The BMP pixel depth ({bitsPerPixel}) is unsupported.");
        bool supportedCompression =
            compression is 0 or 3 ||
            (compression == 1 && bitsPerPixel == 8) ||
            (compression == 2 && bitsPerPixel == 4);
        if (!supportedCompression)
            errors.Add(
                $"The BMP compression mode ({compression}) is not valid for a {bitsPerPixel}-bit game texture.");

        return errors.Count == 0 ? new BmpInfo(width, height, bitsPerPixel) : null;
    }

    private static AssetReplacementValidation ValidateVag(
        ReadOnlySpan<byte> originalData,
        ReadOnlySpan<byte> replacementData)
    {
        List<string> errors = new();
        List<string> warnings = new();
        VagInfo? replacement = TryReadVag(replacementData, errors);
        VagInfo? original = TryReadVag(originalData, new List<string>());

        if (replacement != null && original != null)
        {
            if (replacement.SampleRate != original.SampleRate)
                warnings.Add(
                    $"The sample rate changed from {original.SampleRate:N0} Hz " +
                    $"to {replacement.SampleRate:N0} Hz.");
            if (replacement.Version != original.Version)
                warnings.Add(
                    $"The VAG version changed from 0x{original.Version:X8} " +
                    $"to 0x{replacement.Version:X8}.");
        }

        string description = replacement == null
            ? "PlayStation VAG audio"
            : $"VAG audio — {replacement.SampleRate:N0} Hz, {replacement.DurationSeconds:0.00} seconds";
        return new AssetReplacementValidation(
            AssetReplacementKind.VagAudio, description, errors, warnings);
    }

    private static VagInfo? TryReadVag(ReadOnlySpan<byte> data, ICollection<string> errors)
    {
        if (data.Length < 48 || !data[..4].SequenceEqual("VAGp"u8))
        {
            errors.Add("The replacement does not have a standard VAGp header.");
            return null;
        }

        uint version = ReadBigEndianUInt32(data.Slice(4, 4));
        uint dataSize = ReadBigEndianUInt32(data.Slice(12, 4));
        uint sampleRate = ReadBigEndianUInt32(data.Slice(16, 4));
        if (dataSize == 0 || dataSize > data.Length - 48)
            errors.Add("The VAG header's audio-data size lies outside the file.");
        if (dataSize % 16 != 0)
            errors.Add("VAG ADPCM audio data must contain complete 16-byte frames.");
        if (sampleRate is < 8000 or > 96000)
            errors.Add($"The VAG sample rate ({sampleRate} Hz) is outside the supported range.");

        if (dataSize <= data.Length - 48 && dataSize % 16 == 0)
        {
            ReadOnlySpan<byte> frames = data.Slice(48, (int)dataSize);
            for (int position = 0; position < frames.Length; position += 16)
            {
                int predictor = frames[position] >> 4;
                int shift = frames[position] & 0x0f;
                if (predictor > 4 || shift > 12)
                {
                    errors.Add($"The VAG contains invalid ADPCM frame parameters at byte {position + 48}.");
                    break;
                }
            }
        }

        double duration = sampleRate == 0 ? 0 : dataSize / 16d * 28d / sampleRate;
        return errors.Count == 0 ? new VagInfo(version, dataSize, sampleRate, duration) : null;
    }

    private static AssetReplacementValidation ValidatePss(
        ReadOnlySpan<byte> originalData,
        ReadOnlySpan<byte> replacementData)
    {
        List<string> errors = new();
        List<string> warnings = new();
        PssInfo? replacement = TryReadPss(replacementData, errors);
        PssInfo? original = TryReadPss(originalData, new List<string>());

        if (replacement != null && original != null)
        {
            if (replacement.Width != original.Width || replacement.Height != original.Height)
                errors.Add(
                    $"Video dimensions must remain {original.Width} x {original.Height}; " +
                    $"the replacement is {replacement.Width} x {replacement.Height}.");
            if (replacement.FrameRateCode != original.FrameRateCode)
                warnings.Add("The MPEG frame-rate code differs from the original PSS.");
            if (replacement.HasPrivateAudio != original.HasPrivateAudio)
                warnings.Add(
                    replacement.HasPrivateAudio
                        ? "The replacement adds a private audio stream that the original did not contain."
                        : "The replacement has no private audio stream; the original PSS contained audio.");
        }

        string description = replacement == null
            ? "PlayStation 2 PSS video"
            : $"PSS video — {replacement.Width} x {replacement.Height}" +
              (replacement.HasPrivateAudio ? ", audio stream present" : ", no private audio stream");
        return new AssetReplacementValidation(
            AssetReplacementKind.PssVideo, description, errors, warnings);
    }

    private static PssInfo? TryReadPss(ReadOnlySpan<byte> data, ICollection<string> errors)
    {
        if (data.Length < 64 || !data[..4].SequenceEqual(new byte[] { 0x00, 0x00, 0x01, 0xba }))
        {
            errors.Add("The replacement does not start with an MPEG program-stream pack header.");
            return null;
        }

        int limit = Math.Min(data.Length - 7, 4 * 1024 * 1024);
        int sequence = -1;
        bool hasSystemHeader = false;
        bool hasVideoPacket = false;
        bool hasPicture = false;
        bool hasPrivateAudio = false;
        for (int index = 0; index < limit; index++)
        {
            if (data[index] != 0x00 || data[index + 1] != 0x00 || data[index + 2] != 0x01)
                continue;

            switch (data[index + 3])
            {
                case 0xbb:
                    hasSystemHeader = true;
                    break;
                case 0xbd:
                    hasPrivateAudio = true;
                    break;
                case 0xe0:
                    hasVideoPacket = true;
                    break;
                case 0x00:
                    hasPicture = true;
                    break;
                case 0xb3 when sequence < 0:
                    sequence = index;
                    break;
            }
        }

        if (!hasSystemHeader) errors.Add("The PSS is missing its MPEG system header.");
        if (!hasVideoPacket) errors.Add("The PSS contains no MPEG video packet.");
        if (!hasPicture) errors.Add("The PSS contains no MPEG picture header.");
        if (sequence < 0)
        {
            errors.Add("The PSS contains no MPEG sequence header.");
            return null;
        }

        int width = (data[sequence + 4] << 4) | (data[sequence + 5] >> 4);
        int height = ((data[sequence + 5] & 0x0f) << 8) | data[sequence + 6];
        byte frameRateCode = (byte)(data[sequence + 7] & 0x0f);
        if (width <= 0 || height <= 0 || width > 4096 || height > 4096)
            errors.Add($"The PSS MPEG dimensions ({width} x {height}) are invalid.");
        if (frameRateCode is < 1 or > 8)
            errors.Add($"The PSS MPEG frame-rate code ({frameRateCode}) is invalid.");

        return errors.Count == 0
            ? new PssInfo(width, height, frameRateCode, hasPrivateAudio)
            : null;
    }

    private static uint ReadBigEndianUInt32(ReadOnlySpan<byte> value) =>
        ((uint)value[0] << 24) | ((uint)value[1] << 16) | ((uint)value[2] << 8) | value[3];

    private static uint ReadLittleEndianUInt32(ReadOnlySpan<byte> value) =>
        value[0] | ((uint)value[1] << 8) | ((uint)value[2] << 16) | ((uint)value[3] << 24);

    private static int ReadLittleEndianInt32(ReadOnlySpan<byte> value) =>
        unchecked((int)ReadLittleEndianUInt32(value));

    private static ushort ReadLittleEndianUInt16(ReadOnlySpan<byte> value) =>
        (ushort)(value[0] | (value[1] << 8));

    private sealed record PngInfo(int Width, int Height, byte BitDepth, byte ColorType);
    private sealed record BmpInfo(int Width, int Height, ushort BitsPerPixel);
    private sealed record VagInfo(uint Version, uint DataSize, uint SampleRate, double DurationSeconds);
    private sealed record PssInfo(int Width, int Height, byte FrameRateCode, bool HasPrivateAudio);
}
