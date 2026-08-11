using System.Buffers.Binary;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using PS2_DATA_File_Extractor.Models;

namespace PS2_DATA_File_Extractor.FileOperations;

public sealed class RenderWareAnimationArchive
{
    private readonly string _metPath;
    private readonly RenderWareSkeletonResolver _skeletonResolver;
    private readonly Dictionary<string, byte[]> _textureReplacements =
        new(StringComparer.OrdinalIgnoreCase);

    private RenderWareAnimationArchive(
        string metPath,
        METFileStructure structure,
        List<RenderWareAnimationFile> files)
    {
        _metPath = metPath;
        _skeletonResolver = new RenderWareSkeletonResolver(metPath, structure);
        Files = files;
    }

    public IReadOnlyList<RenderWareAnimationFile> Files { get; }
    public int ChangedAnimationCount => Files.Count(file => file.IsChanged);
    public int ChangedEventCount => PairedEvents.Count(file => file.IsChanged);
    public int ChangedTextureCount => _textureReplacements.Count;
    public int ChangedFileCount => ChangedAnimationCount + ChangedEventCount + ChangedTextureCount;
    public int PairedEventCount => Files.Count(file => file.PairedEvent != null);

    private IReadOnlyList<FacialEventFile> PairedEvents => Files
        .Select(file => file.PairedEvent)
        .OfType<FacialEventFile>()
        .GroupBy(file => file.SourcePath, StringComparer.OrdinalIgnoreCase)
        .Select(group => group.First())
        .ToList();

    public RenderWareAnimationBinding? ResolveSkeleton(RenderWareAnimationFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return _skeletonResolver.Resolve(file);
    }

    public RenderWareSkinnedModel? LoadModel(RenderWareAnimationBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return _skeletonResolver.LoadModel(binding);
    }

    public byte[] GetTextureBytes(RenderWareTexture texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        if (string.IsNullOrWhiteSpace(texture.SourcePath))
            throw new InvalidDataException("This model texture has no DATA.MET source path.");
        return _textureReplacements.TryGetValue(texture.SourcePath, out byte[]? replacement)
            ? replacement.ToArray()
            : _skeletonResolver.ReadEntry(texture.SourcePath);
    }

    public TextureReplacementResult StageTextureReplacement(
        RenderWareTexture target,
        byte[] sourceImage)
    {
        ArgumentNullException.ThrowIfNull(sourceImage);
        return StageTextureReplacementCore(target, sourceImage);
    }

    private TextureReplacementResult StageTextureReplacementCore(
        RenderWareTexture target,
        ReadOnlySpan<byte> sourceImage)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (string.IsNullOrWhiteSpace(target.SourcePath))
            throw new InvalidDataException("This model texture has no replaceable DATA.MET source path.");
        if (sourceImage.IsEmpty || sourceImage.Length > 64 * 1024 * 1024)
            throw new InvalidDataException("The replacement image is empty or larger than 64 MB.");

        byte[] converted;
        int sourceWidth;
        int sourceHeight;
        try
        {
            using MemoryStream input = new(sourceImage.ToArray(), writable: false);
            using Image source = Image.FromStream(input, useEmbeddedColorManagement: false,
                validateImageData: true);
            sourceWidth = source.Width;
            sourceHeight = source.Height;
            using Bitmap bitmap = new(target.Width, target.Height, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                using ImageAttributes attributes = new();
                attributes.SetWrapMode(WrapMode.TileFlipXY);
                graphics.DrawImage(source, new Rectangle(0, 0, target.Width, target.Height),
                    0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
            }
            using MemoryStream output = new();
            bitmap.Save(output, ImageFormat.Png);
            converted = output.ToArray();
        }
        catch (Exception exception) when (exception is ArgumentException or
                                           System.Runtime.InteropServices.ExternalException)
        {
            throw new InvalidDataException("The selected file is not a readable PNG, BMP, or JPEG image.", exception);
        }

        byte[] original = _skeletonResolver.ReadEntry(target.SourcePath);
        AssetReplacementValidation validation = AssetReplacementValidator.Validate(
            target.SourcePath, original, converted);
        if (!validation.IsValid)
            throw new InvalidDataException(validation.FormatErrors());
        RenderWareTexture replacement = RenderWareTexture.Decode(target.SourcePath, converted);
        _textureReplacements[target.SourcePath] = converted;
        _skeletonResolver.ReplaceCachedTexture(target.SourcePath, replacement);
        return new TextureReplacementResult(target.SourcePath, sourceWidth, sourceHeight,
            target.Width, target.Height, sourceWidth != target.Width || sourceHeight != target.Height,
            validation.Warnings);
    }

    public bool IsTextureChanged(string sourcePath) =>
        _textureReplacements.ContainsKey(sourcePath);

    public void ResetTexture(string sourcePath)
    {
        if (!_textureReplacements.Remove(sourcePath)) return;
        _skeletonResolver.ReloadCachedTexture(sourcePath);
    }

    public AnimationSaveResult SaveTextureChangesWithBackup()
    {
        METArchiveBatchSaveResult result = METArchiveBatchEditor.SaveWithBackup(
            _metPath, _textureReplacements, "player-appearance");
        return new AnimationSaveResult(result.BackupPath, result.ChangedEntryCount,
            result.RebuiltArchive);
    }

    public AnimationReplacementCompatibility GetReplacementCompatibility(
        RenderWareAnimationFile target,
        RenderWareAnimationFile source)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);
        if (ReferenceEquals(target, source) ||
            target.SourcePath.Equals(source.SourcePath, StringComparison.OrdinalIgnoreCase))
            return new AnimationReplacementCompatibility(false,
                "Choose a different source animation.", false);
        if (target.TrackCount != source.TrackCount)
            return new AnimationReplacementCompatibility(false,
                $"Track count differs ({target.TrackCount} versus {source.TrackCount}).", false);

        RenderWareAnimationBinding? targetBinding = ResolveSkeleton(target);
        RenderWareAnimationBinding? sourceBinding = ResolveSkeleton(source);
        if (targetBinding == null || sourceBinding == null)
            return new AnimationReplacementCompatibility(false,
                "A compatible DFF/HAnim hierarchy could not be resolved for both animations.", false);
        IReadOnlyList<RenderWareSkeletonBone> targetBones = targetBinding.Skeleton.Bones;
        IReadOnlyList<RenderWareSkeletonBone> sourceBones = sourceBinding.Skeleton.Bones;
        bool sameHierarchy = targetBones.Count == sourceBones.Count &&
                             targetBones.Zip(sourceBones).All(pair =>
                                 pair.First.NodeId == pair.Second.NodeId &&
                                 pair.First.ParentTrackIndex == pair.Second.ParentTrackIndex);
        if (!sameHierarchy)
            return new AnimationReplacementCompatibility(false,
                "The animations use different HAnim bone layouts.", false);

        bool canCopyEvent = CanCopyEvent(source.PairedEvent, target.PairedEvent);
        string scheme = target.SchemeId == source.SchemeId
            ? target.SchemeName
            : $"{source.SchemeName} source will replace {target.SchemeName} data";
        return new AnimationReplacementCompatibility(true,
            $"Compatible {target.TrackCount}-track hierarchy; {scheme}; " +
            $"{source.FrameCount:N0} source keyframes.", canCopyEvent);
    }

    public AnimationReplacementResult ReplaceAnimation(
        RenderWareAnimationFile target,
        RenderWareAnimationFile source,
        bool keepTargetDuration,
        bool copyPairedEvent)
    {
        AnimationReplacementCompatibility compatibility =
            GetReplacementCompatibility(target, source);
        if (!compatibility.IsCompatible)
            throw new InvalidDataException(compatibility.Message);
        if (copyPairedEvent && !compatibility.CanCopyPairedEvent)
            throw new InvalidDataException(
                "The source EVT cannot be copied because one of the animations has no paired EVT or its event definitions differ.");

        float originalTargetDuration = target.DurationSeconds;
        float sourceDuration = source.DurationSeconds;
        target.ReplaceFrom(source, keepTargetDuration ? originalTargetDuration : null);
        bool eventCopied = false;
        if (copyPairedEvent)
        {
            double scale = keepTargetDuration ? originalTargetDuration / sourceDuration : 1D;
            IEnumerable<FacialEvent> events = source.PairedEvent!.Events.Select(item =>
                item with { Timestamp = item.Timestamp * scale });
            target.PairedEvent!.ReplaceEvents(events);
            eventCopied = true;
        }
        return new AnimationReplacementResult(target.SourcePath, source.SourcePath,
            target.DurationSeconds, target.FrameCount, eventCopied);
    }

    public static RenderWareAnimationArchive Load(string metPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(metPath);
        METFileStructure structure = METFileReader.ReadMETFile(metPath);
        List<FileEntry> animationEntries = structure.AllEntries
            .Where(entry => Path.GetExtension(entry.Path)
                .Equals(".anm", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (animationEntries.Count == 0)
            throw new InvalidDataException("This DATA.MET does not contain any RenderWare ANM files.");

        List<RenderWareAnimationFile> files = new(animationEntries.Count);
        List<FacialEventFile> events = new();
        using FileStream stream = new(metPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        foreach (FileEntry entry in animationEntries)
            files.Add(RenderWareAnimationFile.Parse(entry.Path, ReadEntry(stream, entry)));

        foreach (FileEntry entry in structure.AllEntries.Where(entry =>
                     Path.GetExtension(entry.Path).Equals(".evt", StringComparison.OrdinalIgnoreCase)))
        {
            byte[] data = ReadEntry(stream, entry);
            int length = data.Length;
            while (length > 0 && data[length - 1] == 0) length--;
            events.Add(FacialEventFile.Parse(entry.Path, data.AsSpan(0, length)));
        }

        PairEvents(files, events);
        files.Sort((left, right) =>
            StringComparer.OrdinalIgnoreCase.Compare(left.SourcePath, right.SourcePath));
        return new RenderWareAnimationArchive(metPath, structure, files);
    }

    public AnimationSaveResult SaveWithBackup()
    {
        Dictionary<string, byte[]> replacements = Files
            .Where(file => file.IsChanged)
            .ToDictionary(file => file.SourcePath, file => file.Serialize(),
                StringComparer.OrdinalIgnoreCase);
        foreach (FacialEventFile file in PairedEvents.Where(file => file.IsChanged))
            replacements[file.SourcePath] = file.Serialize();
        foreach ((string path, byte[] data) in _textureReplacements)
            replacements[path] = data;
        METArchiveBatchSaveResult result = METArchiveBatchEditor.SaveWithBackup(
            _metPath, replacements, "animations");
        return new AnimationSaveResult(result.BackupPath, result.ChangedEntryCount,
            result.RebuiltArchive);
    }

    public void ResetAll()
    {
        foreach (RenderWareAnimationFile file in Files) file.Reset();
        foreach (FacialEventFile file in PairedEvents) file.Reset();
        foreach (string path in _textureReplacements.Keys.ToList()) ResetTexture(path);
    }

    private static bool CanCopyEvent(FacialEventFile? source, FacialEventFile? target)
    {
        if (source == null || target == null || ReferenceEquals(source, target)) return false;
        return source.Events.All(item =>
            target.ClassDefinitions.TryGetValue(item.EventClass,
                out IReadOnlyList<string>? types) &&
            types.Contains(item.EventType, StringComparer.OrdinalIgnoreCase));
    }

    private static byte[] ReadEntry(FileStream stream, FileEntry entry)
    {
        stream.Position = entry.Offset;
        byte[] data = new byte[entry.OriginalSize];
        stream.ReadExactly(data);
        return data;
    }

    private static void PairEvents(
        IReadOnlyList<RenderWareAnimationFile> animations,
        IReadOnlyList<FacialEventFile> events)
    {
        Dictionary<string, FacialEventFile> exact = events.ToDictionary(
            file => NormalizePath(file.SourcePath), StringComparer.OrdinalIgnoreCase);
        Dictionary<string, List<FacialEventFile>> canonical = new(StringComparer.OrdinalIgnoreCase);
        foreach (FacialEventFile file in events)
        {
            string key = PairingKey(file.SourcePath);
            if (!canonical.TryGetValue(key, out List<FacialEventFile>? matches))
            {
                matches = new List<FacialEventFile>();
                canonical[key] = matches;
            }
            matches.Add(file);
        }

        foreach (RenderWareAnimationFile animation in animations)
        {
            string exactPath = Path.ChangeExtension(
                NormalizePath(animation.SourcePath), ".evt").Replace('\\', '/');
            if (exact.TryGetValue(exactPath, out FacialEventFile? paired))
            {
                animation.PairedEvent = paired;
                continue;
            }
            if (canonical.TryGetValue(PairingKey(animation.SourcePath),
                    out List<FacialEventFile>? matches) && matches.Count == 1)
                animation.PairedEvent = matches[0];
        }
    }

    private static string PairingKey(string sourcePath)
    {
        string normalized = NormalizePath(sourcePath);
        string? directory = Path.GetDirectoryName(normalized)?.Replace('\\', '/');
        string parent = directory == null ? string.Empty : Path.GetFileName(directory);
        string stem = Path.GetFileNameWithoutExtension(normalized);
        if (stem.StartsWith(parent, StringComparison.OrdinalIgnoreCase))
            stem = stem[parent.Length..];
        stem = stem.TrimStart('_');
        if (stem.StartsWith("bat_", StringComparison.OrdinalIgnoreCase))
            stem = stem[4..];
        else if (stem.StartsWith("bat", StringComparison.OrdinalIgnoreCase))
            stem = stem[3..].TrimStart('_');
        return $"{directory}|{stem}";
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');
}

public sealed class RenderWareAnimationFile
{
    public const uint AnimationChunkId = 0x1B;
    public const int AnimationVersion = 0x100;
    public const int StandardScheme = 1;
    public const int CompressedScheme = 2;
    private const int HeaderSize = 32;
    private const int StandardFrameSize = 36;
    private const int CompressedMemoryFrameSize = 24;
    private const int CompressedDiskFrameSize = 22;
    private const int CompressedCustomDataSize = 24;

    private readonly byte[] _originalBytes;
    private byte[]? _replacementBytes;
    private List<RenderWareAnimationKeyFrame>? _frames;
    private List<RenderWareAnimationTrack>? _tracks;
    private float _duration;
    private bool _changed;

    private RenderWareAnimationFile(string sourcePath, byte[] data)
    {
        SourcePath = sourcePath;
        _originalBytes = data;
        ParseHeader(data);
    }

    public string SourcePath { get; }
    public uint RenderWareVersion { get; private set; }
    public int SchemeId { get; private set; }
    public string SchemeName => SchemeId == StandardScheme ? "Standard" : "Compressed";
    public int FrameCount { get; private set; }
    public int Flags { get; private set; }
    public float DurationSeconds => _duration;
    public int TrackCount { get; private set; }
    public FacialEventFile? PairedEvent { get; internal set; }
    public bool IsChanged => _changed;
    public IReadOnlyList<RenderWareAnimationKeyFrame> Frames
    {
        get
        {
            EnsureFrames();
            return _frames!;
        }
    }
    public IReadOnlyList<RenderWareAnimationTrack> Tracks
    {
        get
        {
            EnsureFrames();
            return _tracks!;
        }
    }

    public static RenderWareAnimationFile Parse(string sourcePath, ReadOnlySpan<byte> data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        if (data.Length < HeaderSize)
            throw new InvalidDataException($"'{sourcePath}' is too small to be a RenderWare animation.");
        return new RenderWareAnimationFile(sourcePath, data.ToArray());
    }

    public void ScaleToDuration(float newDuration)
    {
        if (!float.IsFinite(newDuration) || newDuration <= 0)
            throw new InvalidDataException("Animation duration must be a positive finite number.");
        EnsureFrames();
        float ratio = newDuration / _duration;
        foreach (RenderWareAnimationKeyFrame frame in _frames!)
            frame.TimeSeconds *= ratio;
        _duration = newDuration;
        _changed = true;
    }

    public void SetKeyFrameTime(int frameIndex, float timeSeconds)
    {
        EnsureFrames();
        if (frameIndex < 0 || frameIndex >= _frames!.Count)
            throw new ArgumentOutOfRangeException(nameof(frameIndex));
        if (!float.IsFinite(timeSeconds) || timeSeconds < 0 || timeSeconds > _duration)
            throw new InvalidDataException(
                $"Keyframe time must be between 0 and {_duration:0.######} seconds.");

        RenderWareAnimationKeyFrame frame = _frames[frameIndex];
        RenderWareAnimationKeyFrame? previous = frame.PreviousFrameIndex is int previousIndex &&
                                                previousIndex >= 0 &&
                                                previousIndex < _frames.Count &&
                                                _frames[previousIndex].TrackIndex == frame.TrackIndex
            ? _frames[previousIndex]
            : null;
        RenderWareAnimationKeyFrame? next = _frames.FirstOrDefault(candidate =>
            candidate.TrackIndex == frame.TrackIndex &&
            candidate.PreviousFrameIndex == frameIndex);
        if (previous != null && timeSeconds < previous.TimeSeconds)
            throw new InvalidDataException(
                $"Track {frame.TrackIndex} requires a time of at least {previous.TimeSeconds:0.######} seconds.");
        if (next != null && timeSeconds > next.TimeSeconds)
            throw new InvalidDataException(
                $"Track {frame.TrackIndex} requires a time no later than {next.TimeSeconds:0.######} seconds.");
        if (Math.Abs(frame.TimeSeconds - timeSeconds) < 0.0000001F) return;
        frame.TimeSeconds = timeSeconds;
        _changed = true;
        RebuildTrackSummaries();
    }

    public RenderWareAnimationTransform SampleTrack(int trackIndex, float timeSeconds)
    {
        EnsureFrames();
        if (trackIndex < 0 || trackIndex >= _tracks!.Count)
            throw new ArgumentOutOfRangeException(nameof(trackIndex));
        IReadOnlyList<int> indices = _tracks[trackIndex].FrameIndices;
        RenderWareAnimationKeyFrame first = _frames![indices[0]];
        if (timeSeconds <= first.TimeSeconds || indices.Count == 1) return first.Transform;
        RenderWareAnimationKeyFrame last = _frames[indices[^1]];
        if (timeSeconds >= last.TimeSeconds) return last.Transform;
        for (int index = 1; index < indices.Count; index++)
        {
            RenderWareAnimationKeyFrame right = _frames[indices[index]];
            if (timeSeconds > right.TimeSeconds) continue;
            RenderWareAnimationKeyFrame left = _frames[indices[index - 1]];
            float span = right.TimeSeconds - left.TimeSeconds;
            float amount = span <= 0 ? 0 : (timeSeconds - left.TimeSeconds) / span;
            return RenderWareAnimationTransform.Lerp(left.Transform, right.Transform, amount);
        }
        return last.Transform;
    }

    public byte[] Serialize()
    {
        byte[] workingBytes = _replacementBytes ?? _originalBytes;
        if (!_changed) return workingBytes.ToArray();
        EnsureFrames();
        byte[] result = workingBytes.ToArray();
        WriteSingle(result, 28, _duration);
        int diskFrameSize = SchemeId == StandardScheme ? StandardFrameSize : CompressedDiskFrameSize;
        for (int index = 0; index < _frames!.Count; index++)
            WriteSingle(result, HeaderSize + index * diskFrameSize, _frames[index].TimeSeconds);
        return result;
    }

    public void Reset()
    {
        _replacementBytes = null;
        _frames = null;
        _tracks = null;
        _changed = false;
        ParseHeader(_originalBytes);
    }

    internal void ReplaceFrom(RenderWareAnimationFile source, float? fittedDuration)
    {
        ArgumentNullException.ThrowIfNull(source);
        byte[] replacement = source.Serialize();
        RenderWareAnimationFile parsed = Parse(SourcePath, replacement);
        if (parsed.TrackCount != TrackCount)
            throw new InvalidDataException(
                $"The source has {parsed.TrackCount} tracks; the target requires {TrackCount}.");
        _replacementBytes = replacement;
        _frames = null;
        _tracks = null;
        ParseHeader(replacement);
        _changed = true;
        if (fittedDuration.HasValue &&
            Math.Abs(_duration - fittedDuration.Value) > 0.0000001F)
            ScaleToDuration(fittedDuration.Value);
    }

    private void ParseHeader(ReadOnlySpan<byte> data)
    {
        uint chunk = ReadUInt32(data, 0);
        int payloadLength = ReadInt32(data, 4);
        RenderWareVersion = ReadUInt32(data, 8);
        int version = ReadInt32(data, 12);
        SchemeId = ReadInt32(data, 16);
        FrameCount = ReadInt32(data, 20);
        Flags = ReadInt32(data, 24);
        _duration = ReadSingle(data, 28);
        if (chunk != AnimationChunkId || payloadLength != data.Length - 12 ||
            version != AnimationVersion)
            throw new InvalidDataException($"'{SourcePath}' has an invalid RenderWare animation header.");
        if (SchemeId is not (StandardScheme or CompressedScheme))
            throw new InvalidDataException(
                $"'{SourcePath}' uses unsupported interpolation scheme {SchemeId}.");
        if (FrameCount <= 0 || !float.IsFinite(_duration) || _duration <= 0)
            throw new InvalidDataException($"'{SourcePath}' has invalid frame or duration metadata.");
        int expectedLength = SchemeId == StandardScheme
            ? HeaderSize + FrameCount * StandardFrameSize
            : HeaderSize + FrameCount * CompressedDiskFrameSize + CompressedCustomDataSize;
        if (data.Length != expectedLength)
            throw new InvalidDataException(
                $"'{SourcePath}' has {data.Length} bytes; scheme {SchemeId} with {FrameCount} frames requires {expectedLength}.");
        TrackCount = ComputeTrackCount(data);
    }

    private int ComputeTrackCount(ReadOnlySpan<byte> data)
    {
        int diskSize = SchemeId == StandardScheme ? StandardFrameSize : CompressedDiskFrameSize;
        int previousOffset = SchemeId == StandardScheme ? 32 : 18;
        for (int index = 1; index < FrameCount; index++)
        {
            int raw = ReadInt32(data, HeaderSize + index * diskSize + previousOffset);
            if (raw == 0) return index;
        }
        return 1;
    }

    private void EnsureFrames()
    {
        if (_frames != null) return;
        byte[] workingBytes = _replacementBytes ?? _originalBytes;
        _frames = new List<RenderWareAnimationKeyFrame>(FrameCount);
        float[] custom = SchemeId == CompressedScheme
            ? Enumerable.Range(0, 6)
                .Select(index => ReadSingle(workingBytes,
                    HeaderSize + FrameCount * CompressedDiskFrameSize + index * 4))
                .ToArray()
            : Array.Empty<float>();
        int diskSize = SchemeId == StandardScheme ? StandardFrameSize : CompressedDiskFrameSize;
        int memorySize = SchemeId == StandardScheme ? StandardFrameSize : CompressedMemoryFrameSize;
        for (int index = 0; index < FrameCount; index++)
        {
            int offset = HeaderSize + index * diskSize;
            float time = ReadSingle(workingBytes, offset);
            RenderWareAnimationTransform transform;
            int previousRaw;
            if (SchemeId == StandardScheme)
            {
                transform = new RenderWareAnimationTransform(
                    ReadSingle(workingBytes, offset + 4),
                    ReadSingle(workingBytes, offset + 8),
                    ReadSingle(workingBytes, offset + 12),
                    ReadSingle(workingBytes, offset + 16),
                    ReadSingle(workingBytes, offset + 20),
                    ReadSingle(workingBytes, offset + 24),
                    ReadSingle(workingBytes, offset + 28));
                previousRaw = ReadInt32(workingBytes, offset + 32);
            }
            else
            {
                float qx = DecodeCompressedFloat(ReadUInt16(workingBytes, offset + 4));
                float qy = DecodeCompressedFloat(ReadUInt16(workingBytes, offset + 6));
                float qz = DecodeCompressedFloat(ReadUInt16(workingBytes, offset + 8));
                float qw = DecodeCompressedFloat(ReadUInt16(workingBytes, offset + 10));
                float tx = DecodeCompressedFloat(ReadUInt16(workingBytes, offset + 12)) * custom[3] + custom[0];
                float ty = DecodeCompressedFloat(ReadUInt16(workingBytes, offset + 14)) * custom[4] + custom[1];
                float tz = DecodeCompressedFloat(ReadUInt16(workingBytes, offset + 16)) * custom[5] + custom[2];
                transform = new RenderWareAnimationTransform(qx, qy, qz, qw, tx, ty, tz);
                previousRaw = ReadInt32(workingBytes, offset + 18);
            }

            int? previousIndex = previousRaw >= 0 && previousRaw % memorySize == 0
                ? previousRaw / memorySize
                : null;
            int track = index < TrackCount
                ? index
                : previousIndex is int previous && previous < index
                    ? _frames[previous].TrackIndex
                    : throw new InvalidDataException(
                        $"'{SourcePath}' frame {index} has an invalid previous-frame link.");
            if (index < TrackCount) previousIndex = null;
            if (!float.IsFinite(time) || !transform.IsFinite)
                throw new InvalidDataException($"'{SourcePath}' frame {index} contains non-finite values.");
            _frames.Add(new RenderWareAnimationKeyFrame(
                index, track, time, previousIndex, previousRaw, transform));
        }
        RebuildTrackSummaries();
    }

    private void RebuildTrackSummaries()
    {
        List<RenderWareAnimationKeyFrame> frames = _frames!;
        _tracks = Enumerable.Range(0, TrackCount)
            .Select(track =>
            {
                List<int> indices = frames
                    .Where(frame => frame.TrackIndex == track)
                    .OrderBy(frame => frame.TimeSeconds)
                    .ThenBy(frame => frame.Index)
                    .Select(frame => frame.Index)
                    .ToList();
                return new RenderWareAnimationTrack(track, indices,
                    indices.Count == 0 ? 0 : frames[indices[0]].TimeSeconds,
                    indices.Count == 0 ? 0 : frames[indices[^1]].TimeSeconds);
            })
            .ToList();
    }

    private static float DecodeCompressedFloat(ushort value)
    {
        uint bits = (uint)(value & 0x8000) << 16;
        if ((value & 0x7FFF) != 0)
            bits |= (uint)(value & 0x7800) * 0x1000 + 0x38000000U |
                    (uint)(value & 0x07FF) << 12;
        return BitConverter.Int32BitsToSingle(unchecked((int)bits));
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
    private static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
    private static int ReadInt32(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4));
    private static float ReadSingle(ReadOnlySpan<byte> data, int offset) =>
        BitConverter.Int32BitsToSingle(ReadInt32(data, offset));
    private static void WriteSingle(Span<byte> data, int offset, float value) =>
        BinaryPrimitives.WriteInt32LittleEndian(data.Slice(offset, 4),
            BitConverter.SingleToInt32Bits(value));
}

public sealed class RenderWareAnimationKeyFrame
{
    internal RenderWareAnimationKeyFrame(
        int index,
        int trackIndex,
        float timeSeconds,
        int? previousFrameIndex,
        int previousFrameOffset,
        RenderWareAnimationTransform transform)
    {
        Index = index;
        TrackIndex = trackIndex;
        TimeSeconds = timeSeconds;
        PreviousFrameIndex = previousFrameIndex;
        PreviousFrameOffset = previousFrameOffset;
        Transform = transform;
    }

    public int Index { get; }
    public int TrackIndex { get; }
    public float TimeSeconds { get; internal set; }
    public int? PreviousFrameIndex { get; }
    public int PreviousFrameOffset { get; }
    public RenderWareAnimationTransform Transform { get; }
}

public sealed record RenderWareAnimationTrack(
    int Index,
    IReadOnlyList<int> FrameIndices,
    float StartTime,
    float EndTime);

public readonly record struct RenderWareAnimationTransform(
    float QuaternionX,
    float QuaternionY,
    float QuaternionZ,
    float QuaternionW,
    float TranslationX,
    float TranslationY,
    float TranslationZ)
{
    public bool IsFinite =>
        float.IsFinite(QuaternionX) && float.IsFinite(QuaternionY) &&
        float.IsFinite(QuaternionZ) && float.IsFinite(QuaternionW) &&
        float.IsFinite(TranslationX) && float.IsFinite(TranslationY) &&
        float.IsFinite(TranslationZ);

    public static RenderWareAnimationTransform Lerp(
        RenderWareAnimationTransform left,
        RenderWareAnimationTransform right,
        float amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        (float qx, float qy, float qz, float qw) = SlerpQuaternion(left, right, amount);
        return new RenderWareAnimationTransform(
            qx, qy, qz, qw,
            Lerp(left.TranslationX, right.TranslationX, amount),
            Lerp(left.TranslationY, right.TranslationY, amount),
            Lerp(left.TranslationZ, right.TranslationZ, amount));
    }

    private static (float X, float Y, float Z, float W) SlerpQuaternion(
        RenderWareAnimationTransform left,
        RenderWareAnimationTransform right,
        float amount)
    {
        float rx = right.QuaternionX;
        float ry = right.QuaternionY;
        float rz = right.QuaternionZ;
        float rw = right.QuaternionW;
        float dot = left.QuaternionX * rx + left.QuaternionY * ry +
                    left.QuaternionZ * rz + left.QuaternionW * rw;
        if (dot < 0)
        {
            dot = -dot;
            rx = -rx; ry = -ry; rz = -rz; rw = -rw;
        }

        float x;
        float y;
        float z;
        float w;
        if (dot > 0.9995F)
        {
            x = Lerp(left.QuaternionX, rx, amount);
            y = Lerp(left.QuaternionY, ry, amount);
            z = Lerp(left.QuaternionZ, rz, amount);
            w = Lerp(left.QuaternionW, rw, amount);
        }
        else
        {
            float angle = MathF.Acos(Math.Clamp(dot, -1F, 1F));
            float denominator = MathF.Sin(angle);
            float leftWeight = MathF.Sin((1F - amount) * angle) / denominator;
            float rightWeight = MathF.Sin(amount * angle) / denominator;
            x = left.QuaternionX * leftWeight + rx * rightWeight;
            y = left.QuaternionY * leftWeight + ry * rightWeight;
            z = left.QuaternionZ * leftWeight + rz * rightWeight;
            w = left.QuaternionW * leftWeight + rw * rightWeight;
        }

        float length = MathF.Sqrt(x * x + y * y + z * z + w * w);
        return length <= 0.000001F ? (0, 0, 0, 1) :
            (x / length, y / length, z / length, w / length);
    }

    private static float Lerp(float left, float right, float amount) =>
        left + (right - left) * amount;
}

public sealed record AnimationSaveResult(
    string? BackupPath,
    int ChangedFileCount,
    bool RebuiltArchive);

public sealed record AnimationReplacementCompatibility(
    bool IsCompatible,
    string Message,
    bool CanCopyPairedEvent);

public sealed record AnimationReplacementResult(
    string TargetPath,
    string SourcePath,
    float DurationSeconds,
    int FrameCount,
    bool EventCopied);

public sealed record TextureReplacementResult(
    string SourcePath,
    int SourceWidth,
    int SourceHeight,
    int TargetWidth,
    int TargetHeight,
    bool WasResized,
    IReadOnlyList<string> Warnings);
