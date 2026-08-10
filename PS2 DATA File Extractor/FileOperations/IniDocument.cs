using System.Globalization;

namespace PS2_DATA_File_Extractor.FileOperations;

/// <summary>
/// Minimal, formatting-preserving reader for the simple INI dialect used by Backyard Baseball.
/// </summary>
public sealed class IniDocument
{
    private readonly List<string> _lines;
    private readonly string _newLine;
    private readonly bool _endsWithNewLine;

    private IniDocument(List<string> lines, string newLine, bool endsWithNewLine)
    {
        _lines = lines;
        _newLine = newLine;
        _endsWithNewLine = endsWithNewLine;
    }

    public IReadOnlyList<IniSetting> Settings => ReadSettings();

    public static IniDocument Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        string newLine = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        bool endsWithNewLine = text.EndsWith("\n", StringComparison.Ordinal);
        string normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        List<string> lines = normalized.Split('\n').ToList();
        if (endsWithNewLine && lines.Count > 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return new IniDocument(lines, newLine, endsWithNewLine);
    }

    public bool SetValue(string section, string key, string value)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);

        IniSetting? setting = ReadSettings().FirstOrDefault(candidate =>
            candidate.Section.Equals(section, StringComparison.OrdinalIgnoreCase) &&
            candidate.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (setting is null)
        {
            return false;
        }

        string line = _lines[setting.LineIndex];
        _lines[setting.LineIndex] = line[..setting.ValueStart] + value +
            line[(setting.ValueStart + setting.ValueLength)..];
        return true;
    }

    public override string ToString()
    {
        string text = string.Join(_newLine, _lines);
        return _endsWithNewLine ? text + _newLine : text;
    }

    private List<IniSetting> ReadSettings()
    {
        List<IniSetting> settings = new List<IniSetting>();
        string section = string.Empty;

        for (int lineIndex = 0; lineIndex < _lines.Count; lineIndex++)
        {
            string line = _lines[lineIndex];
            string trimmed = line.Trim();
            if (trimmed.StartsWith("[", StringComparison.Ordinal) &&
                trimmed.EndsWith("]", StringComparison.Ordinal) && trimmed.Length > 2)
            {
                section = trimmed[1..^1].Trim();
                continue;
            }

            if (trimmed.Length == 0 || trimmed.StartsWith(';') || trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            int equalsIndex = line.IndexOf('=');
            if (equalsIndex < 0)
            {
                continue;
            }

            string key = line[..equalsIndex].Trim();
            if (key.Length == 0)
            {
                continue;
            }

            int commentIndex = line.IndexOf(';', equalsIndex + 1);
            int valueAreaEnd = commentIndex >= 0 ? commentIndex : line.Length;
            int valueStart = equalsIndex + 1;
            while (valueStart < valueAreaEnd && char.IsWhiteSpace(line[valueStart]))
            {
                valueStart++;
            }

            int valueEnd = valueAreaEnd;
            while (valueEnd > valueStart && char.IsWhiteSpace(line[valueEnd - 1]))
            {
                valueEnd--;
            }

            settings.Add(new IniSetting(
                section,
                key,
                line[valueStart..valueEnd],
                lineIndex,
                valueStart,
                valueEnd - valueStart));
        }

        return settings;
    }
}

public sealed record IniSetting(
    string Section,
    string Key,
    string Value,
    int LineIndex,
    int ValueStart,
    int ValueLength);

public enum GameplayTweakValueKind
{
    Boolean,
    Integer,
    Decimal,
    Text
}

public static class GameplayTweakValue
{
    public static GameplayTweakValueKind DetectKind(string value)
    {
        if (bool.TryParse(value, out _))
        {
            return GameplayTweakValueKind.Boolean;
        }

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            return GameplayTweakValueKind.Integer;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            return GameplayTweakValueKind.Decimal;
        }

        return GameplayTweakValueKind.Text;
    }

    public static bool TryNormalize(
        GameplayTweakValueKind kind,
        string? value,
        out string normalized,
        out string error)
    {
        normalized = (value ?? string.Empty).Trim();
        error = string.Empty;

        switch (kind)
        {
            case GameplayTweakValueKind.Boolean:
                if (!bool.TryParse(normalized, out bool booleanValue))
                {
                    error = "Enter True or False.";
                    return false;
                }

                normalized = booleanValue ? "True" : "False";
                return true;

            case GameplayTweakValueKind.Integer:
                if (!int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                {
                    error = "Enter a whole number using digits and an optional minus sign.";
                    return false;
                }

                return true;

            case GameplayTweakValueKind.Decimal:
                if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ||
                    double.IsNaN(number) || double.IsInfinity(number))
                {
                    error = "Enter a finite number using a period as the decimal separator.";
                    return false;
                }

                return true;

            default:
                if (normalized.Contains('\r') || normalized.Contains('\n'))
                {
                    error = "A value cannot contain a line break.";
                    return false;
                }

                return true;
        }
    }
}
