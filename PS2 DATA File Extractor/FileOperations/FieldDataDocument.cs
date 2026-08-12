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

        int pendingCommentLine = -1;
        for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            FieldDataTextLine line = lines[lineIndex];
            string trimmed = line.Content.Trim();
            if (section == null)
            {
                if (trimmed.StartsWith("//", StringComparison.Ordinal))
                {
                    pendingComment = trimmed[2..].Trim();
                    pendingCommentLine = lineIndex;
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
                        currentAmbient.StartLineIndex = lineIndex;
                        currentAmbient.LeadingCommentLineIndex = string.IsNullOrWhiteSpace(inlineComment)
                            ? pendingCommentLine : -1;
                        ambients.Add(currentAmbient);
                    }
                    pendingComment = string.Empty;
                    pendingCommentLine = -1;
                    continue;
                }

                if (trimmed.Length > 0)
                {
                    pendingComment = string.Empty;
                    pendingCommentLine = -1;
                }
                continue;
            }

            if (trimmed == "}")
            {
                if (currentAmbient != null) currentAmbient.EndLineIndex = lineIndex;
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

    public FieldDataDocument CloneAmbient(int ambientIndex, string? comment = null)
    {
        FieldDataAmbient source = AmbientAt(ambientIndex);
        string name = string.IsNullOrWhiteSpace(comment) ? $"Copy of {source.DisplayName}" : comment.Trim();
        return InsertEnabledAmbientBlock(RenderAmbientBlock(source, name));
    }

    public FieldDataDocument CloneAmbientFrom(
        FieldDataDocument sourceDocument,
        int sourceAmbientIndex,
        string? comment = null)
    {
        ArgumentNullException.ThrowIfNull(sourceDocument);
        FieldDataAmbient source = sourceDocument.AmbientAt(sourceAmbientIndex);
        string name = string.IsNullOrWhiteSpace(comment) ? $"Copy of {source.DisplayName}" : comment.Trim();
        return InsertEnabledAmbientBlock(sourceDocument.RenderAmbientBlock(source, name));
    }

    public FieldDataDocument AddAmbient(
        string comment,
        IEnumerable<KeyValuePair<string, string>> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string newline = PreferredNewline();
        StringBuilder block = new();
        block.Append("amb { // ").Append(SafeComment(comment)).Append(newline);
        foreach ((string key, string value) in settings)
        {
            if (!Regex.IsMatch(key, "^[A-Za-z][A-Za-z0-9]*$"))
                throw new ArgumentException($"'{key}' is not a valid fielddata directive name.", nameof(settings));
            if (!FieldDataValue.TryNormalize(GetValueKind(key), value, out string normalized, out string error))
                throw new ArgumentException($"Invalid {key} value: {error}", nameof(settings));
            block.Append('\t').Append(key);
            if (normalized.Length > 0) block.Append(' ').Append(normalized);
            block.Append(';').Append(newline);
        }
        block.Append('}').Append(newline);
        return InsertEnabledAmbientBlock(block.ToString());
    }

    public FieldDataDocument RemoveAmbient(int ambientIndex)
    {
        FieldDataAmbient ambient = AmbientAt(ambientIndex);
        int start = ambient.LeadingCommentLineIndex >= 0
            ? ambient.LeadingCommentLineIndex : ambient.StartLineIndex;
        if (start < 0 || ambient.EndLineIndex < start)
            throw new InvalidDataException("The selected ambient block has incomplete source boundaries.");
        StringBuilder text = new();
        for (int index = 0; index < _lines.Count; index++)
        {
            if (index >= start && index <= ambient.EndLineIndex) continue;
            text.Append(_lines[index].Render()).Append(_lines[index].Terminator);
        }
        FieldDataDocument result = Parse(text.ToString());
        int loaded = DeclaredAmbientCount - (ambientIndex < DeclaredAmbientCount ? 1 : 0);
        if (!result.TrySetDeclaredAmbientCount(Math.Clamp(loaded, 0, result.Ambients.Count)))
            throw new InvalidDataException("The fielddata file has no editable numAmbs directive.");
        return result;
    }

    public FieldDataDocument SetAmbientSetting(int ambientIndex, string key, string value)
    {
        FieldDataAmbient ambient = AmbientAt(ambientIndex);
        FieldDataValueKind kind = GetValueKind(key);
        if (!FieldDataValue.TryNormalize(kind, value, out string normalized, out string error))
            throw new ArgumentException($"Invalid {key} value: {error}", nameof(value));
        FieldDataSetting? existing = ambient.Settings.FirstOrDefault(setting =>
            setting.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.Value = normalized;
            return this;
        }
        if (!Regex.IsMatch(key, "^[A-Za-z][A-Za-z0-9]*$"))
            throw new ArgumentException($"'{key}' is not a valid fielddata directive name.", nameof(key));
        StringBuilder text = new();
        for (int index = 0; index < _lines.Count; index++)
        {
            if (index == ambient.EndLineIndex)
            {
                string newline = _lines[index].Terminator.Length > 0
                    ? _lines[index].Terminator : PreferredNewline();
                text.Append('\t').Append(key);
                if (normalized.Length > 0) text.Append(' ').Append(normalized);
                text.Append(';').Append(newline);
            }
            text.Append(_lines[index].Render()).Append(_lines[index].Terminator);
        }
        return Parse(text.ToString());
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

    private FieldDataAmbient AmbientAt(int index)
    {
        if (index < 0 || index >= Ambients.Count) throw new ArgumentOutOfRangeException(nameof(index));
        return Ambients[index];
    }

    private string RenderAmbientBlock(FieldDataAmbient ambient, string comment)
    {
        if (ambient.StartLineIndex < 0 || ambient.EndLineIndex < ambient.StartLineIndex)
            throw new InvalidDataException("The selected ambient block has incomplete source boundaries.");
        StringBuilder result = new();
        for (int index = ambient.StartLineIndex; index <= ambient.EndLineIndex; index++)
        {
            FieldDataTextLine line = _lines[index];
            string content = index == ambient.StartLineIndex
                ? Regex.Replace(line.Render(), @"^(?<indent>\s*)amb\s*\{.*$",
                    match => $"{match.Groups["indent"].Value}amb {{ // {SafeComment(comment)}",
                    RegexOptions.IgnoreCase)
                : line.Render();
            result.Append(content).Append(line.Terminator.Length == 0 ? PreferredNewline() : line.Terminator);
        }
        return result.ToString();
    }

    private FieldDataDocument InsertEnabledAmbientBlock(string block)
    {
        string newline = PreferredNewline();
        int insertAmbient = Math.Clamp(DeclaredAmbientCount, 0, Ambients.Count);
        int insertLine = insertAmbient < Ambients.Count
            ? (Ambients[insertAmbient].LeadingCommentLineIndex >= 0
                ? Ambients[insertAmbient].LeadingCommentLineIndex
                : Ambients[insertAmbient].StartLineIndex)
            : _lines.Count;
        StringBuilder prefix = new(), suffix = new();
        for (int index = 0; index < _lines.Count; index++)
        {
            StringBuilder target = index < insertLine ? prefix : suffix;
            target.Append(_lines[index].Render()).Append(_lines[index].Terminator);
        }
        string before = prefix.ToString();
        if (before.Length > 0 && !before.EndsWith('\n') && !before.EndsWith('\r')) before += newline;
        if (before.Length > 0 && !before.EndsWith(newline + newline, StringComparison.Ordinal)) before += newline;
        string inserted = block;
        if (!inserted.EndsWith('\n') && !inserted.EndsWith('\r')) inserted += newline;
        if (suffix.Length > 0 && !inserted.EndsWith(newline + newline, StringComparison.Ordinal)) inserted += newline;
        FieldDataDocument result = Parse(before + inserted + suffix);
        int loaded = Math.Min(result.Ambients.Count, DeclaredAmbientCount + 1);
        if (!result.TrySetDeclaredAmbientCount(loaded))
            throw new InvalidDataException("The fielddata file has no editable numAmbs directive.");
        return result;
    }

    private string PreferredNewline() =>
        _lines.Select(line => line.Terminator).FirstOrDefault(value => value.Length > 0) ?? Environment.NewLine;

    private static string SafeComment(string value)
    {
        string result = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ')
            .Replace('{', '(').Replace('}', ')').Trim();
        return result.Length == 0 ? "New ambient object" : result;
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
    internal int StartLineIndex { get; set; } = -1;
    internal int EndLineIndex { get; set; } = -1;
    internal int LeadingCommentLineIndex { get; set; } = -1;
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
