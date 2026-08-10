using System.Globalization;
using System.Text;

namespace PS2_DATA_File_Extractor.FileOperations;

/// <summary>
/// Converts binary payloads to and from an editable hexadecimal representation.
/// </summary>
public static class HexDataCodec
{
    public static string Format(ReadOnlySpan<byte> data, int bytesPerLine = 16)
    {
        if (bytesPerLine <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bytesPerLine));
        }

        StringBuilder result = new StringBuilder(data.Length * 3);
        for (int index = 0; index < data.Length; index++)
        {
            if (index > 0)
            {
                result.Append(index % bytesPerLine == 0 ? Environment.NewLine : " ");
            }

            result.Append(data[index].ToString("X2", CultureInfo.InvariantCulture));
        }

        return result.ToString();
    }

    public static bool TryParse(string text, out byte[] data, out string error)
    {
        List<byte> bytes = new List<byte>();
        string[] tokens = text.Split(
            new[] { ' ', '\t', '\r', '\n', ',', ';' },
            StringSplitOptions.RemoveEmptyEntries);

        for (int tokenIndex = 0; tokenIndex < tokens.Length; tokenIndex++)
        {
            string token = tokens[tokenIndex];
            if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                token = token[2..];
            }

            if (token.Length == 0 || token.Length % 2 != 0)
            {
                data = Array.Empty<byte>();
                error = $"Hex token {tokenIndex + 1} ('{tokens[tokenIndex]}') must contain pairs of hexadecimal digits.";
                return false;
            }

            for (int index = 0; index < token.Length; index += 2)
            {
                if (!byte.TryParse(token.AsSpan(index, 2), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out byte value))
                {
                    data = Array.Empty<byte>();
                    error = $"Hex token {tokenIndex + 1} ('{tokens[tokenIndex]}') contains an invalid byte.";
                    return false;
                }

                bytes.Add(value);
            }
        }

        data = bytes.ToArray();
        error = string.Empty;
        return true;
    }
}
