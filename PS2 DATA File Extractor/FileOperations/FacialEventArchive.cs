using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using PS2_DATA_File_Extractor.Models;

namespace PS2_DATA_File_Extractor.FileOperations;

public sealed class FacialEventArchive
{
    private readonly string _metPath;
    private readonly METFileStructure _structure;
    private readonly IReadOnlyDictionary<string, FacialTextureCatalog> _textureCatalogs;

    private FacialEventArchive(
        string metPath,
        METFileStructure structure,
        List<FacialEventFile> files,
        IReadOnlyDictionary<string, FacialTextureCatalog> textureCatalogs)
    {
        _metPath = metPath;
        _structure = structure;
        Files = files;
        _textureCatalogs = textureCatalogs;
    }

    public IReadOnlyList<FacialEventFile> Files { get; }
    public int ChangedFileCount => Files.Count(file => file.IsChanged);

    public static FacialEventArchive Load(string metPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(metPath);
        METFileStructure structure = METFileReader.ReadMETFile(metPath);
        List<FileEntry> entries = structure.AllEntries
            .Where(entry => Path.GetExtension(entry.Path).Equals(".evt", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (entries.Count == 0)
            throw new InvalidDataException("This DATA.MET does not contain any EVT facial-event files.");

        HashSet<string> paths = structure.AllEntries
            .Select(entry => NormalizePath(entry.Path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<FacialEventFile> files = new(entries.Count);
        using FileStream stream = new(metPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        foreach (FileEntry entry in entries)
        {
            stream.Position = entry.Offset;
            byte[] data = new byte[entry.OriginalSize];
            stream.ReadExactly(data);
            int length = data.Length;
            while (length > 0 && data[length - 1] == 0) length--;
            FacialEventFile file = FacialEventFile.Parse(entry.Path, data.AsSpan(0, length));
            string vagPath = Path.ChangeExtension(NormalizePath(entry.Path), ".vag");
            if (paths.Contains(vagPath)) file.PairedVagPath = vagPath;
            files.Add(file);
        }

        files.Sort((left, right) =>
            StringComparer.OrdinalIgnoreCase.Compare(left.SourcePath, right.SourcePath));
        return new FacialEventArchive(metPath, structure, files, BuildTextureCatalogs(structure));
    }

    public FacialEventTextureSet? LoadTextureSet(FacialEventFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        string path = NormalizePath(file.SourcePath);
        const string battingRoot = "data/batting/";
        if (!path.StartsWith(battingRoot, StringComparison.OrdinalIgnoreCase)) return null;
        int directoryEnd = path.IndexOf('/', battingRoot.Length);
        if (directoryEnd < 0) return null;
        string characterCode = path[battingRoot.Length..directoryEnd];
        if (!_textureCatalogs.TryGetValue(characterCode, out FacialTextureCatalog? catalog)) return null;

        Dictionary<int, FacialEventTexture> eyes = new();
        Dictionary<int, FacialEventTexture> mouths = new();
        using FileStream stream = new(_metPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        foreach (FacialTextureEntry item in catalog.Entries)
        {
            stream.Position = item.Entry.Offset;
            byte[] data = new byte[item.Entry.OriginalSize];
            stream.ReadExactly(data);
            FacialEventTexture texture = new(item.Entry.Path, item.Pose, data);
            (item.IsEyes ? eyes : mouths)[item.Pose] = texture;
        }

        return new FacialEventTextureSet(characterCode, catalog.CharacterName, eyes, mouths);
    }

    public Ps2AudioInfo? InspectPairedAudio(FacialEventFile file)
    {
        FileEntry? entry = GetPairedVagEntry(file);
        return entry == null ? null : Ps2AudioArchive.Inspect(_metPath, entry, _structure);
    }

    public byte[] DecodePairedAudio(FacialEventFile file)
    {
        FileEntry entry = GetPairedVagEntry(file)
            ?? throw new InvalidDataException($"'{file.SourcePath}' has no same-name VAG voice clip.");
        return Ps2AudioArchive.DecodeToWave(_metPath, entry, _structure);
    }

    public FacialEventSaveResult SaveWithBackup()
    {
        Dictionary<string, byte[]> replacements = Files
            .Where(file => file.IsChanged)
            .ToDictionary(file => file.SourcePath, file => file.Serialize(), StringComparer.OrdinalIgnoreCase);
        METArchiveBatchSaveResult result = METArchiveBatchEditor.SaveWithBackup(
            _metPath, replacements, "facial-events");
        return new FacialEventSaveResult(result.BackupPath, result.ChangedEntryCount, result.RebuiltArchive);
    }

    public void ResetAll()
    {
        foreach (FacialEventFile file in Files) file.Reset();
    }

    private FileEntry? GetPairedVagEntry(FacialEventFile file)
    {
        if (file.PairedVagPath == null) return null;
        return _structure.AllEntries.FirstOrDefault(entry =>
            NormalizePath(entry.Path).Equals(file.PairedVagPath, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyDictionary<string, FacialTextureCatalog> BuildTextureCatalogs(
        METFileStructure structure)
    {
        Dictionary<string, List<FacialTextureEntry>> entries =
            new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> names = new(StringComparer.OrdinalIgnoreCase);
        Regex pattern = new(
            @"^data/batting/(?<code>[^/]+)/(?<name>.+)_(?<kind>eyes|mouth)_tx\.(?<pose>\d+)\.png$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        foreach (FileEntry entry in structure.AllEntries)
        {
            Match match = pattern.Match(NormalizePath(entry.Path));
            if (!match.Success ||
                !int.TryParse(match.Groups["pose"].Value, NumberStyles.None,
                    CultureInfo.InvariantCulture, out int pose) || pose <= 0) continue;
            string code = match.Groups["code"].Value;
            if (!entries.TryGetValue(code, out List<FacialTextureEntry>? characterEntries))
            {
                characterEntries = new List<FacialTextureEntry>();
                entries[code] = characterEntries;
            }
            characterEntries.Add(new FacialTextureEntry(
                entry, pose,
                match.Groups["kind"].Value.Equals("eyes", StringComparison.OrdinalIgnoreCase)));
            names.TryAdd(code, match.Groups["name"].Value);
        }

        return entries.ToDictionary(
            pair => pair.Key,
            pair => new FacialTextureCatalog(
                names.GetValueOrDefault(pair.Key, pair.Key),
                pair.Value.OrderBy(item => item.IsEyes ? 0 : 1).ThenBy(item => item.Pose).ToList()),
            StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    private sealed record FacialTextureEntry(FileEntry Entry, int Pose, bool IsEyes);
    private sealed record FacialTextureCatalog(string CharacterName, IReadOnlyList<FacialTextureEntry> Entries);
}

public sealed class FacialEventFile
{
    private readonly byte[] _originalBytes;
    private XDocument _document = null!;
    private List<FacialEvent> _events = new();
    private Dictionary<string, IReadOnlyList<string>> _classDefinitions = new(StringComparer.OrdinalIgnoreCase);
    private bool _changed;
    private string _newLine = "\r\n";
    private bool _usesBareValueLines;

    private FacialEventFile(string sourcePath, byte[] originalBytes)
    {
        SourcePath = sourcePath;
        _originalBytes = originalBytes;
        LoadDocument();
    }

    public string SourcePath { get; }
    public string? PairedVagPath { get; internal set; }
    public IReadOnlyList<FacialEvent> Events => _events;
    public IReadOnlyDictionary<string, IReadOnlyList<string>> ClassDefinitions => _classDefinitions;
    public IReadOnlyList<string> EventClasses => _classDefinitions.Keys.OrderBy(value => value).ToList();
    public bool IsChanged => _changed;
    public bool IsTalkie => _classDefinitions.ContainsKey("CLASS_TALKIES");
    public double DurationSeconds => _events.Count == 0 ? 0 : _events.Max(item => item.Timestamp);
    public string Kind => IsTalkie
        ? "Talkie lip sync"
        : _classDefinitions.ContainsKey("CLASS_MOUTH")
            ? "Animation face events"
            : "Facial events";

    public static FacialEventFile Parse(string sourcePath, ReadOnlySpan<byte> data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        if (data.IsEmpty) throw new InvalidDataException($"'{sourcePath}' is empty.");
        return new FacialEventFile(sourcePath, data.ToArray());
    }

    public IReadOnlyList<string> GetEventTypes(string eventClass) =>
        _classDefinitions.TryGetValue(eventClass, out IReadOnlyList<string>? types)
            ? types
            : Array.Empty<string>();

    public FacialEvent? GetActiveEvent(string eventClass, double positionSeconds) =>
        _events.LastOrDefault(item =>
            item.EventClass.Equals(eventClass, StringComparison.OrdinalIgnoreCase) &&
            item.Timestamp <= positionSeconds + 0.000001);

    public void ReplaceEvents(IEnumerable<FacialEvent> events)
    {
        List<FacialEvent> replacement = events.ToList();
        ValidateEvents(replacement);
        if (replacement.SequenceEqual(_events)) return;
        XElement root = _document.Root
            ?? throw new InvalidDataException($"'{SourcePath}' has no XML root element.");

        foreach (XElement element in root.Elements("event").ToList())
        {
            if (element.PreviousNode is XText whitespace && string.IsNullOrWhiteSpace(whitespace.Value))
                whitespace.Remove();
            element.Remove();
        }
        if (root.LastNode is XText trailing && string.IsNullOrWhiteSpace(trailing.Value))
            trailing.Remove();

        foreach (FacialEvent item in replacement)
        {
            root.Add(new XText(_newLine + "\t"));
            root.Add(CreateElement(item));
        }
        root.Add(new XText(_newLine));
        _events = replacement;
        _changed = true;
    }

    public byte[] Serialize()
    {
        ValidateEvents(_events);
        using MemoryStream stream = new();
        XmlWriterSettings settings = new()
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = false,
            NewLineHandling = NewLineHandling.None,
            OmitXmlDeclaration = false
        };
        using (XmlWriter writer = XmlWriter.Create(stream, settings))
            _document.Save(writer);
        byte[] serialized = stream.ToArray();
        if (!_usesBareValueLines) return serialized;
        string text = Encoding.UTF8.GetString(serialized);
        text = Regex.Replace(
            text,
            @"(?m)^(\s*)(value|elementID)(\s+value=""[^""]*"")/&gt;(\r?)$",
            "$1$2$3/>$4",
            RegexOptions.CultureInvariant);
        return Encoding.UTF8.GetBytes(text);
    }

    public void Reset()
    {
        LoadDocument();
        _changed = false;
    }

    public override string ToString() => SourcePath;

    private void LoadDocument()
    {
        string text = Encoding.UTF8.GetString(_originalBytes);
        _newLine = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        try
        {
            _document = XDocument.Parse(text, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        }
        catch (Exception exception) when (exception is XmlException or ArgumentException)
        {
            throw new InvalidDataException($"'{SourcePath}' is not valid EVT XML: {exception.Message}", exception);
        }

        XElement root = _document.Root
            ?? throw new InvalidDataException($"'{SourcePath}' has no XML root element.");
        if (!root.Name.LocalName.Equals("event_stream", StringComparison.Ordinal))
            throw new InvalidDataException($"'{SourcePath}' does not use the event_stream EVT root.");

        Dictionary<string, IReadOnlyList<string>> definitions = new(StringComparer.OrdinalIgnoreCase);
        XElement? classes = root.Element("classes");
        if (classes == null) throw new InvalidDataException($"'{SourcePath}' has no EVT class definitions.");
        foreach (XElement definition in classes.Elements("classdef"))
        {
            string name = RequiredAttribute(definition, "name");
            definitions[name] = definition.Elements("eventdef")
                .Select(item => RequiredAttribute(item, "name"))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        if (definitions.Count == 0)
            throw new InvalidDataException($"'{SourcePath}' has no supported EVT classes.");
        _classDefinitions = definitions;
        _events = root.Elements("event").Select(ParseEvent).ToList();
        // A few retail files use ROOT without listing it in their local classdef.
        // Preserve any type the file actually uses and offer it in the editor.
        foreach (IGrouping<string, FacialEvent> group in _events.GroupBy(
                     item => item.EventClass, StringComparer.OrdinalIgnoreCase))
        {
            if (!_classDefinitions.TryGetValue(group.Key, out IReadOnlyList<string>? declared)) continue;
            _classDefinitions[group.Key] = declared.Concat(group.Select(item => item.EventType))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        ValidateEvents(_events);
    }

    private FacialEvent ParseEvent(XElement element)
    {
        double timestamp = ParseDouble(RequiredValue(element, "timestamp"), "timestamp");
        string eventClass = RequiredValue(element, "eventClass");
        string eventType = RequiredValue(element, "eventType");
        double value = ParseDouble(RequiredEventValue(element, "value"), "value");
        if (!int.TryParse(RequiredEventValue(element, "elementID"), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int elementId))
            throw new InvalidDataException($"'{SourcePath}' contains an invalid elementID.");
        return new FacialEvent(timestamp, eventClass, eventType, value, elementId);
    }

    private void ValidateEvents(IReadOnlyList<FacialEvent> events)
    {
        Dictionary<string, double> previousByClass = new(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < events.Count; index++)
        {
            FacialEvent item = events[index];
            if (!double.IsFinite(item.Timestamp) || item.Timestamp < 0)
                throw new InvalidDataException($"Event {index + 1} has an invalid timestamp.");
            double previous = previousByClass.GetValueOrDefault(item.EventClass, -1);
            if (item.Timestamp + 0.000001 < previous)
                throw new InvalidDataException(
                    $"EVT timestamps for {item.EventClass} must stay in ascending order.");
            if (!_classDefinitions.TryGetValue(item.EventClass, out IReadOnlyList<string>? types))
                throw new InvalidDataException($"Event {index + 1} uses unknown class '{item.EventClass}'.");
            if (!types.Contains(item.EventType, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"Event {index + 1} uses unknown type '{item.EventType}' for {item.EventClass}.");
            if (!double.IsFinite(item.Value))
                throw new InvalidDataException($"Event {index + 1} has an invalid value.");
            if (item.ElementId < 0)
                throw new InvalidDataException($"Event {index + 1} has a negative element ID.");
            previousByClass[item.EventClass] = item.Timestamp;
        }
    }

    private XElement CreateElement(FacialEvent item) =>
        new("event",
            new XText(_newLine + "\t\t"), ValueElement("timestamp", FormatTimestamp(item.Timestamp)),
            new XText(_newLine + "\t\t"), ValueElement("eventClass", item.EventClass),
            new XText(_newLine + "\t\t"), ValueElement("eventType", item.EventType),
            new XText(_newLine + "\t\t"), ValueNode("value", item.Value.ToString("0.0###", CultureInfo.InvariantCulture)),
            new XText(_newLine + "\t\t"), ValueNode("elementID", item.ElementId.ToString(CultureInfo.InvariantCulture)),
            new XText(_newLine + "\t"));

    private static XElement ValueElement(string name, string value) =>
        new(name, new XAttribute("value", value));

    private XObject ValueNode(string name, string value) => _usesBareValueLines
        ? new XText($"{name} value=\"{value}\"/>")
        : ValueElement(name, value);

    private string RequiredEventValue(XElement parent, string name)
    {
        XElement? element = parent.Element(name);
        if (element != null) return RequiredAttribute(element, "value");
        string text = string.Concat(parent.Nodes().OfType<XText>().Select(node => node.Value));
        Match match = Regex.Match(text,
            $@"(?:^|\s){Regex.Escape(name)}\s+value=""([^""]+)""\s*/>",
            RegexOptions.CultureInvariant);
        if (match.Success)
        {
            _usesBareValueLines = true;
            return match.Groups[1].Value;
        }
        throw new InvalidDataException($"'{SourcePath}' contains an event without {name}.");
    }

    private string RequiredValue(XElement parent, string name)
    {
        XElement? element = parent.Element(name);
        return element == null
            ? throw new InvalidDataException($"'{SourcePath}' contains an event without {name}.")
            : RequiredAttribute(element, "value");
    }

    private static string RequiredAttribute(XElement element, string name) =>
        element.Attribute(name)?.Value
        ?? throw new InvalidDataException($"EVT element '{element.Name}' has no {name} attribute.");

    private double ParseDouble(string value, string field)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result) ||
            !double.IsFinite(result))
            throw new InvalidDataException($"'{SourcePath}' contains an invalid {field} value '{value}'.");
        return result;
    }

    private string FormatTimestamp(double value) =>
        value.ToString(IsTalkie ? "0.00#" : "0.#######", CultureInfo.InvariantCulture);
}

public sealed record FacialEvent(
    double Timestamp,
    string EventClass,
    string EventType,
    double Value,
    int ElementId);

public sealed record FacialEventSaveResult(
    string? BackupPath,
    int ChangedFileCount,
    bool RebuiltArchive);
