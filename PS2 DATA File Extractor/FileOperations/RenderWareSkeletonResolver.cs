using System.Buffers.Binary;
using System.Numerics;
using PS2_DATA_File_Extractor.Models;

namespace PS2_DATA_File_Extractor.FileOperations;

public sealed class RenderWareSkeletonResolver
{
    private readonly string _metPath;
    private readonly IReadOnlyList<FileEntry> _entries;
    private readonly IReadOnlyList<FileEntry> _models;
    private readonly Dictionary<string, RenderWareSkeleton?> _cache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RenderWareSkinnedModel?> _modelCache =
        new(StringComparer.OrdinalIgnoreCase);

    public RenderWareSkeletonResolver(string metPath, METFileStructure structure)
    {
        _metPath = metPath;
        _entries = structure.AllEntries;
        _models = _entries
            .Where(entry => Path.GetExtension(entry.Path)
                .Equals(".dff", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public RenderWareAnimationBinding? Resolve(RenderWareAnimationFile animation)
    {
        string normalized = Normalize(animation.SourcePath);
        string[] parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) return null;
        string category = parts[1];
        string code = parts.Length >= 4
            ? parts[^2]
            : Path.GetFileNameWithoutExtension(normalized).Split('_')[0];
        string preferredCategory = PreferredModelCategory(category);
        IEnumerable<FileEntry> candidates;
        if (category.Equals("batting", StringComparison.OrdinalIgnoreCase) &&
            animation.TrackCount == 5)
        {
            candidates = _models.Where(entry =>
                Normalize(entry.Path).Equals("data/models/batmesh.dff", StringComparison.OrdinalIgnoreCase) ||
                Normalize(entry.Path).Equals("data/models/woodbatmesh.dff", StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            candidates = _models.Where(entry =>
            {
                string modelPath = Normalize(entry.Path);
                string modelStem = Path.GetFileNameWithoutExtension(modelPath);
                string[] modelParts = modelPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                string modelCategory = modelParts.Length > 1 ? modelParts[1] : string.Empty;
                return modelPath.Contains($"/{code}/", StringComparison.OrdinalIgnoreCase) ||
                       modelCategory.Equals(category, StringComparison.OrdinalIgnoreCase) &&
                       modelStem.StartsWith(code, StringComparison.OrdinalIgnoreCase);
            });
        }
        candidates = candidates
            .OrderByDescending(entry => Score(entry.Path, code, category, preferredCategory))
            .ThenByDescending(entry => Normalize(entry.Path)
                .Equals("data/models/batmesh.dff", StringComparison.OrdinalIgnoreCase))
            .ThenBy(entry => entry.OriginalSize);

        foreach (FileEntry entry in candidates)
        {
            RenderWareSkeleton? skeleton = Load(entry);
            if (skeleton?.BoneCount == animation.TrackCount)
                return new RenderWareAnimationBinding(entry.Path, skeleton);
        }
        return null;
    }

    public RenderWareSkinnedModel? LoadModel(RenderWareAnimationBinding binding)
    {
        if (_modelCache.TryGetValue(binding.ModelPath, out RenderWareSkinnedModel? cached))
            return cached;
        FileEntry? modelEntry = _models.FirstOrDefault(entry =>
            Normalize(entry.Path).Equals(Normalize(binding.ModelPath), StringComparison.OrdinalIgnoreCase));
        if (modelEntry == null) return null;
        try
        {
            RenderWareSkinnedModel model = RenderWareSkinnedModel.Parse(
                modelEntry.Path, ReadEntry(modelEntry));
            HashSet<string> textureNames = model.Meshes
                .SelectMany(mesh => mesh.Materials)
                .Select(material => material.TextureName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            IEnumerable<FileEntry> textureEntries = _entries
                .Where(entry => Path.GetExtension(entry.Path)
                    .Equals(".png", StringComparison.OrdinalIgnoreCase))
                .Where(entry => IsNeededTexture(
                    Path.GetFileNameWithoutExtension(entry.Path), textureNames))
                .GroupBy(entry => Path.GetFileNameWithoutExtension(entry.Path),
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group
                    .OrderByDescending(entry => TextureScore(entry.Path, modelEntry.Path))
                    .First());
            foreach (FileEntry textureEntry in textureEntries)
            {
                string stem = Path.GetFileNameWithoutExtension(textureEntry.Path);
                try
                {
                    model.AddTexture(stem, RenderWareTexture.Decode(ReadEntry(textureEntry)));
                }
                catch (Exception exception) when (exception is ArgumentException or
                                                   System.Runtime.InteropServices.ExternalException)
                {
                    // Leave a malformed optional texture unresolved; the material-color fallback remains usable.
                }
            }
            _modelCache[binding.ModelPath] = model;
            return model;
        }
        catch (InvalidDataException)
        {
            _modelCache[binding.ModelPath] = null;
            return null;
        }
    }

    private static bool IsNeededTexture(string stem, IReadOnlySet<string> textureNames) =>
        textureNames.Contains(stem) || textureNames.Any(name =>
        {
            int dot = name.LastIndexOf('.');
            string baseName = dot >= 0 ? name[..dot] : name;
            return stem.StartsWith(baseName + ".", StringComparison.OrdinalIgnoreCase);
        });

    private static int TextureScore(string texturePath, string modelPath)
    {
        string textureDirectory = Path.GetDirectoryName(Normalize(texturePath))?
            .Replace('\\', '/') ?? string.Empty;
        string modelDirectory = Path.GetDirectoryName(Normalize(modelPath))?
            .Replace('\\', '/') ?? string.Empty;
        if (textureDirectory.Equals(modelDirectory, StringComparison.OrdinalIgnoreCase)) return 100_000;
        if (textureDirectory.Equals(modelDirectory + "/textures", StringComparison.OrdinalIgnoreCase))
            return 95_000;

        string[] textureParts = textureDirectory.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string[] modelParts = modelDirectory.Split('/', StringSplitOptions.RemoveEmptyEntries);
        int common = 0;
        while (common < textureParts.Length && common < modelParts.Length &&
               textureParts[common].Equals(modelParts[common], StringComparison.OrdinalIgnoreCase))
            common++;
        int score = common * 1_000;
        string modelCode = modelParts.LastOrDefault() ?? string.Empty;
        if (textureParts.Any(part => part.Equals(modelCode, StringComparison.OrdinalIgnoreCase)))
            score += 20_000;
        string modelCategory = modelParts.Length > 1 ? modelParts[1] : string.Empty;
        string preferredCategory = PreferredModelCategory(modelCategory);
        if (textureParts.Length > 1 &&
            textureParts[1].Equals(preferredCategory, StringComparison.OrdinalIgnoreCase))
            score += 10_000;
        return score;
    }

    private RenderWareSkeleton? Load(FileEntry entry)
    {
        if (_cache.TryGetValue(entry.Path, out RenderWareSkeleton? cached)) return cached;
        try
        {
            using FileStream stream = new(_metPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            stream.Position = entry.Offset;
            byte[] data = new byte[entry.OriginalSize];
            stream.ReadExactly(data);
            RenderWareSkeleton skeleton = RenderWareSkeleton.Parse(entry.Path, data);
            _cache[entry.Path] = skeleton;
            return skeleton;
        }
        catch (InvalidDataException)
        {
            _cache[entry.Path] = null;
            return null;
        }
    }

    private byte[] ReadEntry(FileEntry entry)
    {
        using FileStream stream = new(_metPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        stream.Position = entry.Offset;
        byte[] data = new byte[entry.OriginalSize];
        stream.ReadExactly(data);
        return data;
    }

    private static int Score(
        string path, string code, string animationCategory, string preferredCategory)
    {
        string normalized = Normalize(path);
        string[] parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string modelCategory = parts.Length > 1 ? parts[1] : string.Empty;
        string stem = Path.GetFileNameWithoutExtension(normalized);
        int score = 0;
        if (modelCategory.Equals(animationCategory, StringComparison.OrdinalIgnoreCase)) score += 1000;
        if (modelCategory.Equals(preferredCategory, StringComparison.OrdinalIgnoreCase)) score += 800;
        if (stem.Equals(code, StringComparison.OrdinalIgnoreCase)) score += 300;
        if (stem.StartsWith(code, StringComparison.OrdinalIgnoreCase)) score += 180;
        if (stem.Contains(preferredCategory.TrimEnd('s'), StringComparison.OrdinalIgnoreCase)) score += 80;
        if (stem.StartsWith("hp", StringComparison.OrdinalIgnoreCase) ||
            stem.StartsWith("lp", StringComparison.OrdinalIgnoreCase)) score -= 200;
        return score;
    }

    private static string PreferredModelCategory(string animationCategory) =>
        animationCategory.ToLowerInvariant() switch
        {
            "batting" => "batting",
            "playercard" or "kids" => "playercard",
            "fielding" or "fieldanims" or "baserunning" or "celebration" or
                "darts" or "pitching" => "fielding",
            _ => animationCategory
        };

    private static string Normalize(string path) => path.Replace('\\', '/');
}

public sealed class RenderWareSkeleton
{
    private const uint ClumpChunk = 0x10;
    private const uint StructChunk = 0x01;
    private const uint ExtensionChunk = 0x03;
    private const uint FrameListChunk = 0x0E;
    private const uint HAnimPlugin = 0x11E;
    private const int FrameRecordSize = 56;

    private RenderWareSkeleton(string sourcePath, IReadOnlyList<RenderWareSkeletonBone> bones)
    {
        SourcePath = sourcePath;
        Bones = bones;
    }

    public string SourcePath { get; }
    public IReadOnlyList<RenderWareSkeletonBone> Bones { get; }
    public int BoneCount => Bones.Count;

    public static RenderWareSkeleton Parse(string sourcePath, ReadOnlySpan<byte> data)
    {
        if (data.Length < 24 || ReadUInt32(data, 0) != ClumpChunk)
            throw new InvalidDataException($"'{sourcePath}' is not a RenderWare DFF clump.");
        int clumpEnd = CheckedChunkEnd(data, 0);
        int frameListOffset = FindChild(data, 12, clumpEnd, FrameListChunk);
        if (frameListOffset < 0)
            throw new InvalidDataException($"'{sourcePath}' has no RenderWare frame list.");
        return ParseFrameList(sourcePath, data, frameListOffset);
    }

    private static RenderWareSkeleton ParseFrameList(
        string sourcePath, ReadOnlySpan<byte> data, int frameListOffset)
    {
        int frameListEnd = CheckedChunkEnd(data, frameListOffset);
        int structureOffset = frameListOffset + 12;
        if (structureOffset + 12 > frameListEnd ||
            ReadUInt32(data, structureOffset) != StructChunk)
            throw new InvalidDataException($"'{sourcePath}' has an invalid frame-list structure.");
        int structureEnd = CheckedChunkEnd(data, structureOffset);
        int payload = structureOffset + 12;
        int frameCount = ReadInt32(data, payload);
        if (frameCount <= 0 || structureEnd != payload + 4 + frameCount * FrameRecordSize)
            throw new InvalidDataException($"'{sourcePath}' has invalid DFF frame records.");

        DffFrame[] frames = new DffFrame[frameCount];
        for (int index = 0; index < frameCount; index++)
        {
            int offset = payload + 4 + index * FrameRecordSize;
            frames[index] = new DffFrame(
                ReadInt32(data, offset + 48),
                new Vector3(ReadSingle(data, offset + 36),
                    ReadSingle(data, offset + 40), ReadSingle(data, offset + 44)));
        }

        Dictionary<int, int> nodeIdByFrame = new();
        HAnimHierarchy? hierarchy = null;
        int extensionOffset = structureEnd;
        for (int frame = 0; frame < frameCount; frame++)
        {
            if (extensionOffset + 12 > frameListEnd ||
                ReadUInt32(data, extensionOffset) != ExtensionChunk)
                throw new InvalidDataException($"'{sourcePath}' is missing frame extension {frame}.");
            int extensionEnd = CheckedChunkEnd(data, extensionOffset);
            for (int plugin = extensionOffset + 12; plugin < extensionEnd;)
            {
                int pluginEnd = CheckedChunkEnd(data, plugin);
                if (ReadUInt32(data, plugin) == HAnimPlugin)
                {
                    int pluginLength = ReadInt32(data, plugin + 4);
                    if (pluginLength < 12)
                        throw new InvalidDataException($"'{sourcePath}' has a short HAnim plugin.");
                    int pluginPayload = plugin + 12;
                    int nodeId = ReadInt32(data, pluginPayload + 4);
                    int nodeCount = ReadInt32(data, pluginPayload + 8);
                    nodeIdByFrame[frame] = nodeId;
                    if (nodeCount > 0)
                    {
                        if (pluginLength != 20 + nodeCount * 12)
                            throw new InvalidDataException($"'{sourcePath}' has invalid HAnim hierarchy data.");
                        List<HAnimNode> nodes = new(nodeCount);
                        for (int index = 0; index < nodeCount; index++)
                        {
                            int nodeOffset = pluginPayload + 20 + index * 12;
                            nodes.Add(new HAnimNode(
                                ReadInt32(data, nodeOffset),
                                ReadInt32(data, nodeOffset + 4)));
                        }
                        hierarchy = new HAnimHierarchy(nodes);
                    }
                }
                plugin = pluginEnd;
            }
            extensionOffset = extensionEnd;
        }

        if (hierarchy == null || hierarchy.Nodes.Count == 0)
            throw new InvalidDataException($"'{sourcePath}' has no HAnim skeleton hierarchy.");
        Dictionary<int, int> trackByNodeId = hierarchy.Nodes.ToDictionary(
            node => node.NodeId, node => node.TrackIndex);
        if (trackByNodeId.Count != hierarchy.Nodes.Count ||
            hierarchy.Nodes.Any(node => node.TrackIndex < 0 ||
                                        node.TrackIndex >= hierarchy.Nodes.Count) ||
            hierarchy.Nodes.Select(node => node.TrackIndex).Distinct().Count() != hierarchy.Nodes.Count)
            throw new InvalidDataException($"'{sourcePath}' has invalid HAnim node indices.");

        RenderWareSkeletonBone?[] ordered = new RenderWareSkeletonBone[hierarchy.Nodes.Count];
        foreach (KeyValuePair<int, int> frameNode in nodeIdByFrame)
        {
            if (!trackByNodeId.TryGetValue(frameNode.Value, out int track)) continue;
            int parentTrack = FindParentTrack(frameNode.Key, frames, nodeIdByFrame, trackByNodeId);
            ordered[track] = new RenderWareSkeletonBone(
                track, frameNode.Value, parentTrack, frames[frameNode.Key].BindTranslation);
        }
        if (ordered.Any(bone => bone == null))
            throw new InvalidDataException($"'{sourcePath}' does not map every HAnim node to a DFF frame.");
        return new RenderWareSkeleton(sourcePath, ordered.Select(bone => bone!).ToList());
    }

    private static int FindParentTrack(
        int frameIndex,
        IReadOnlyList<DffFrame> frames,
        IReadOnlyDictionary<int, int> nodeIdByFrame,
        IReadOnlyDictionary<int, int> trackByNodeId)
    {
        int parent = frames[frameIndex].ParentFrame;
        HashSet<int> visited = new();
        while (parent >= 0 && parent < frames.Count && visited.Add(parent))
        {
            if (nodeIdByFrame.TryGetValue(parent, out int nodeId) &&
                trackByNodeId.TryGetValue(nodeId, out int track)) return track;
            parent = frames[parent].ParentFrame;
        }
        return -1;
    }

    private static int FindChild(ReadOnlySpan<byte> data, int start, int end, uint chunkId)
    {
        for (int offset = start; offset < end;)
        {
            if (offset + 12 > end) return -1;
            int chunkEnd = CheckedChunkEnd(data, offset);
            if (chunkEnd > end) return -1;
            if (ReadUInt32(data, offset) == chunkId) return offset;
            offset = chunkEnd;
        }
        return -1;
    }

    private static int CheckedChunkEnd(ReadOnlySpan<byte> data, int offset)
    {
        if (offset < 0 || offset + 12 > data.Length)
            throw new InvalidDataException("RenderWare chunk header is outside the DFF payload.");
        int length = ReadInt32(data, offset + 4);
        long end = (long)offset + 12 + length;
        if (length < 0 || end > data.Length)
            throw new InvalidDataException("RenderWare chunk extends beyond the DFF payload.");
        return (int)end;
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
    private static int ReadInt32(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4));
    private static float ReadSingle(ReadOnlySpan<byte> data, int offset) =>
        BitConverter.Int32BitsToSingle(ReadInt32(data, offset));

    private sealed record DffFrame(int ParentFrame, Vector3 BindTranslation);
    private sealed record HAnimNode(int NodeId, int TrackIndex);
    private sealed record HAnimHierarchy(IReadOnlyList<HAnimNode> Nodes);
}

public sealed record RenderWareSkeletonBone(
    int TrackIndex,
    int NodeId,
    int ParentTrackIndex,
    Vector3 BindTranslation);

public sealed class RenderWareAnimationBinding
{
    public RenderWareAnimationBinding(string modelPath, RenderWareSkeleton skeleton)
    {
        ModelPath = modelPath;
        Skeleton = skeleton;
    }

    public string ModelPath { get; }
    public RenderWareSkeleton Skeleton { get; }

    public IReadOnlyList<Vector3> SamplePose(
        RenderWareAnimationFile animation, float timeSeconds)
        => SampleWorldMatrices(animation, timeSeconds)
            .Select(matrix => matrix.Translation).ToList();

    public IReadOnlyList<Matrix4x4> SampleWorldMatrices(
        RenderWareAnimationFile animation, float timeSeconds)
    {
        if (animation.TrackCount != Skeleton.BoneCount)
            throw new InvalidDataException("The animation and skeleton track counts do not match.");
        Matrix4x4?[] world = new Matrix4x4?[Skeleton.BoneCount];
        bool[] visiting = new bool[Skeleton.BoneCount];
        for (int track = 0; track < Skeleton.BoneCount; track++)
            BuildWorld(track, animation, timeSeconds, world, visiting);
        return world.Select(matrix => matrix!.Value).ToList();
    }

    private Matrix4x4 BuildWorld(
        int track,
        RenderWareAnimationFile animation,
        float timeSeconds,
        Matrix4x4?[] world,
        bool[] visiting)
    {
        if (world[track] is Matrix4x4 cached) return cached;
        if (visiting[track]) throw new InvalidDataException("The DFF skeleton contains a parent cycle.");
        visiting[track] = true;
        RenderWareAnimationTransform transform = animation.SampleTrack(track, timeSeconds);
        Quaternion rotation = new(transform.QuaternionX, transform.QuaternionY,
            transform.QuaternionZ, transform.QuaternionW);
        rotation = rotation.LengthSquared() < 0.000001F ? Quaternion.Identity : Quaternion.Normalize(rotation);
        Matrix4x4 local = Matrix4x4.CreateFromQuaternion(rotation);
        local.Translation = new Vector3(
            transform.TranslationX, transform.TranslationY, transform.TranslationZ);
        int parent = Skeleton.Bones[track].ParentTrackIndex;
        Matrix4x4 result = parent >= 0
            ? local * BuildWorld(parent, animation, timeSeconds, world, visiting)
            : local;
        visiting[track] = false;
        world[track] = result;
        return result;
    }
}
