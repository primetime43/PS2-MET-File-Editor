using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PS2_DATA_File_Extractor.FileOperations;

public sealed class FieldDataDocument
{
    private static readonly Regex DirectivePattern = new(
        @"^(?<indent>\s*)(?<key>[A-Za-z][A-Za-z0-9]*)(?<gap>\s*)(?<value>.*);(?<suffix>\s*(?://.*)?)$",
        RegexOptions.Compiled);
    private readonly List<FieldDataTextLine> _lines;

    private FieldDataDocument(
        List<FieldDataTextLine> lines,
        List<FieldDataSetting> fieldSettings,
        List<FieldDataSetting> collisionSettings,
        List<FieldDataAmbient> ambients)
    {
        _lines = lines;
        FieldSettings = fieldSettings;
        CollisionSettings = collisionSettings;
        Ambients = ambients;
    }

    public IReadOnlyList<FieldDataSetting> FieldSettings { get; }
    public IReadOnlyList<FieldDataSetting> CollisionSettings { get; }
    public IReadOnlyList<FieldDataAmbient> Ambients { get; }
    public int DeclaredAmbientCount => int.TryParse(
        FieldSettings.FirstOrDefault(setting => setting.Key.Equals("numAmbs", StringComparison.OrdinalIgnoreCase))?.Value,
        NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) ? count : 0;

    public static FieldDataDocument Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        List<FieldDataTextLine> lines = SplitLines(text);
        List<FieldDataSetting> fieldSettings = new();
        List<FieldDataSetting> collisionSettings = new();
        List<FieldDataAmbient> ambients = new();
        FieldDataSectionKind? section = null;
        FieldDataAmbient? currentAmbient = null;
        string pendingComment = string.Empty;

        foreach (FieldDataTextLine line in lines)
        {
            string trimmed = line.Content.Trim();
            if (section == null)
            {
                if (trimmed.StartsWith("//", StringComparison.Ordinal))
                {
                    pendingComment = trimmed[2..].Trim();
                    continue;
                }

                Match block = Regex.Match(trimmed, @"^(field|collision|amb)\s*\{\s*(?://\s*(?<comment>.*))?$", RegexOptions.IgnoreCase);
                if (block.Success)
                {
                    section = block.Groups[1].Value.ToLowerInvariant() switch
                    {
                        "field" => FieldDataSectionKind.Field,
                        "collision" => FieldDataSectionKind.Collision,
                        _ => FieldDataSectionKind.Ambient
                    };
                    if (section == FieldDataSectionKind.Ambient)
                    {
                        string inlineComment = block.Groups["comment"].Value.Trim();
                        currentAmbient = new FieldDataAmbient(ambients.Count,
                            string.IsNullOrWhiteSpace(inlineComment) ? pendingComment : inlineComment);
                        ambients.Add(currentAmbient);
                    }
                    pendingComment = string.Empty;
                    continue;
                }

                if (trimmed.Length > 0) pendingComment = string.Empty;
                continue;
            }

            if (trimmed == "}")
            {
                section = null;
                currentAmbient = null;
                continue;
            }

            Match directive = DirectivePattern.Match(line.Content);
            if (!directive.Success) continue;
            string key = directive.Groups["key"].Value;
            FieldDataSetting setting = new(
                line,
                section.Value,
                key,
                directive.Groups["indent"].Value,
                directive.Groups["gap"].Value,
                directive.Groups["value"].Value.Trim(),
                directive.Groups["suffix"].Value,
                GetValueKind(key));

            switch (section)
            {
                case FieldDataSectionKind.Field:
                    fieldSettings.Add(setting);
                    break;
                case FieldDataSectionKind.Collision:
                    collisionSettings.Add(setting);
                    break;
                case FieldDataSectionKind.Ambient:
                    currentAmbient!.SettingsInternal.Add(setting);
                    break;
            }
        }

        if (fieldSettings.Count == 0)
        {
            throw new InvalidDataException("The text does not contain a readable field { } section.");
        }
        return new FieldDataDocument(lines, fieldSettings, collisionSettings, ambients);
    }

    public bool TrySetDeclaredAmbientCount(int count)
    {
        FieldDataSetting? setting = FieldSettings.FirstOrDefault(candidate =>
            candidate.Key.Equals("numAmbs", StringComparison.OrdinalIgnoreCase));
        if (setting == null) return false;
        setting.Value = count.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    public override string ToString()
    {
        StringBuilder result = new();
        foreach (FieldDataTextLine line in _lines)
        {
            result.Append(line.Render());
            result.Append(line.Terminator);
        }
        return result.ToString();
    }

    private static List<FieldDataTextLine> SplitLines(string text)
    {
        List<FieldDataTextLine> lines = new();
        int start = 0;
        for (int index = 0; index < text.Length; index++)
        {
            if (text[index] != '\r' && text[index] != '\n') continue;
            string terminator;
            if (text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
            {
                terminator = "\r\n";
                lines.Add(new FieldDataTextLine(text[start..index], terminator));
                index++;
            }
            else
            {
                terminator = text[index].ToString();
                lines.Add(new FieldDataTextLine(text[start..index], terminator));
            }
            start = index + 1;
        }
        if (start < text.Length) lines.Add(new FieldDataTextLine(text[start..], string.Empty));
        return lines;
    }

    private static FieldDataValueKind GetValueKind(string key) => key.ToLowerInvariant() switch
    {
        "numambs" or "particleactive" or "crowdload" => FieldDataValueKind.Integer,
        "speed" or "hrdelay" or "crowdheight" or "crowdcheertime" => FieldDataValueKind.Number,
        "amblight" or "campos" or "camhpr" or "commpos" or "commhpr" or "pos" or "hpr" or
        "relposhpr" or "randfloatspeed" or "collision" or "startcolor" or "endcolor" or
        "crowdrowcol" or "crowddensityuv" => FieldDataValueKind.NumericList,
        "ballsplash" => FieldDataValueKind.Flag,
        _ => FieldDataValueKind.Text
    };
}

public sealed class FieldDataAmbient
{
    internal FieldDataAmbient(int index, string comment)
    {
        Index = index;
        Comment = comment;
    }

    internal List<FieldDataSetting> SettingsInternal { get; } = new();
    public int Index { get; }
    public string Comment { get; }
    public IReadOnlyList<FieldDataSetting> Settings => SettingsInternal;
    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Comment)) return Comment;
            FieldDataSetting? asset = Settings.FirstOrDefault(setting =>
                setting.Key.Equals("model", StringComparison.OrdinalIgnoreCase) ||
                setting.Key.Equals("particle", StringComparison.OrdinalIgnoreCase) ||
                setting.Key.Equals("movie", StringComparison.OrdinalIgnoreCase));
            return asset == null ? $"Ambient {Index + 1}" : $"Ambient {Index + 1}: {asset.Value}";
        }
    }
}

public sealed class FieldDataSetting
{
    private readonly FieldDataTextLine _line;
    private readonly string _indent;
    private readonly string _gap;
    private readonly string _suffix;
    private string _value;

    internal FieldDataSetting(
        FieldDataTextLine line,
        FieldDataSectionKind section,
        string key,
        string indent,
        string gap,
        string value,
        string suffix,
        FieldDataValueKind kind)
    {
        _line = line;
        Section = section;
        Key = key;
        _indent = indent;
        _gap = gap;
        _value = value;
        _suffix = suffix;
        OriginalValue = value;
        Kind = kind;
        line.Setting = this;
    }

    public FieldDataSectionKind Section { get; }
    public string Key { get; }
    public string FriendlyName => FieldDataValue.Humanize(Key);
    public string OriginalValue { get; }
    public FieldDataValueKind Kind { get; }
    public string Value
    {
        get => _value;
        set => _value = value;
    }
    public bool IsChanged => !OriginalValue.Equals(Value, StringComparison.Ordinal);

    internal string Render()
    {
        string gap = Value.Length == 0 ? string.Empty : (_gap.Length == 0 ? " " : _gap);
        return $"{_indent}{Key}{gap}{Value};{_suffix}";
    }
}

public enum FieldDataSectionKind
{
    Field,
    Collision,
    Ambient
}

public enum FieldDataValueKind
{
    Text,
    Integer,
    Number,
    NumericList,
    Flag
}

public static class FieldDataValue
{
    public static bool TryNormalize(FieldDataValueKind kind, string input, out string normalized, out string error)
    {
        normalized = input.Trim();
        error = string.Empty;
        if (normalized.IndexOfAny(new[] { '\r', '\n', '{', '}' }) >= 0)
        {
            error = "Values cannot contain line breaks or braces.";
            return false;
        }

        switch (kind)
        {
            case FieldDataValueKind.Flag:
                if (normalized.Length != 0)
                {
                    error = "This is a presence flag and must have an empty value.";
                    return false;
                }
                break;
            case FieldDataValueKind.Integer:
                if (!int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                {
                    error = "Enter a whole number.";
                    return false;
                }
                break;
            case FieldDataValueKind.Number:
                if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ||
                    double.IsNaN(number) || double.IsInfinity(number))
                {
                    error = "Enter a finite number using a period as the decimal separator.";
                    return false;
                }
                break;
            case FieldDataValueKind.NumericList:
                string[] values = normalized.Replace(';', ' ')
                    .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (values.Length == 0 || values.Any(value =>
                        !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double item) ||
                        double.IsNaN(item) || double.IsInfinity(item)))
                {
                    error = "Enter one or more finite numbers separated by spaces (semicolons may separate groups).";
                    return false;
                }
                break;
        }
        return true;
    }

    public static string Humanize(string key)
    {
        StringBuilder result = new(key.Length + 8);
        for (int index = 0; index < key.Length; index++)
        {
            char current = key[index];
            if (index > 0 && char.IsUpper(current) && char.IsLower(key[index - 1])) result.Append(' ');
            result.Append(current);
        }
        return result.ToString();
    }
}

internal sealed class FieldDataTextLine
{
    public FieldDataTextLine(string content, string terminator)
    {
        Content = content;
        Terminator = terminator;
    }

    public string Content { get; }
    public string Terminator { get; }
    public FieldDataSetting? Setting { get; set; }
    public string Render() => Setting?.Render() ?? Content;
}
