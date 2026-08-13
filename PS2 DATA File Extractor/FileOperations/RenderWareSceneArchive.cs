using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using PS2_DATA_File_Extractor.Models;

namespace PS2_DATA_File_Extractor.FileOperations;

public enum RenderWareAssetKind { DffModel, RwsScene }

public sealed record RenderWareAssetFile(FileEntry Entry, RenderWareAssetKind Kind, string Category)
{
    public string Path => Entry.Path;
    public int Size => Entry.OriginalSize;
    public string DisplayName => $"{System.IO.Path.GetFileName(Entry.Path)}  [{Entry.OriginalSize / 1024D:0.#} KB]";
}

public sealed class RenderWareSceneArchive
{
    private static readonly IReadOnlyDictionary<string, string> StadiumScenePaths =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["aquadome"] = "data/fields/aquadome_rws/aquadome_ps2.rws",
            ["boardwalk"] = "data/fields/boardwalk_rws/boardwalk_ps2.rws",
            ["desert"] = "data/fields/desert_rws/desert_ps2.rws",
            ["desertnight"] = "data/fields/desertnight_rws/desert_ps2_night.rws",
            ["drivein"] = "data/fields/drivein_rws/drivein_field.rws",
            ["driveinnight"] = "data/fields/driveinnight_rws/drivein_night.rws",
            ["frazier"] = "data/fields/frazier_rws/frazier_field.rws",
            ["gatorflats"] = "data/fields/gatorflats_rws/gatorflats.rws",
            ["gatorflatsnight"] = "data/fields/gatorflatsnight_rws/gatorflats_night.rws",
            ["memorial"] = "data/fields/memorial_rws/hem_field.rws",
            ["quantum"] = "data/fields/quantum_rws/quantum_field.rws",
            ["scrapyard"] = "data/fields/scrapyard_rws/scrap_ps2.rws",
            ["steele"] = "data/fields/steele_rws/steel_ps2.rws",
            ["wheeler"] = "data/fields/wheeler_rws/wheeler_ps2.rws",
            ["wheelernight"] = "data/fields/wheelernight_rws/wheeler_ps2_night.rws"
        };

    private readonly string _metPath;
    private readonly IReadOnlyList<FileEntry> _entries;

    private RenderWareSceneArchive(string metPath, METFileStructure structure)
    {
        _metPath = metPath;
        _entries = structure.AllEntries;
        Assets = _entries
            .Where(entry => Path.GetExtension(entry.Path).Equals(".dff", StringComparison.OrdinalIgnoreCase) ||
                            Path.GetExtension(entry.Path).Equals(".rws", StringComparison.OrdinalIgnoreCase))
            .Select(entry => new RenderWareAssetFile(entry,
                Path.GetExtension(entry.Path).Equals(".rws", StringComparison.OrdinalIgnoreCase)
                    ? RenderWareAssetKind.RwsScene : RenderWareAssetKind.DffModel,
                GetCategory(entry.Path)))
            .OrderBy(asset => asset.Path, StringComparer.OrdinalIgnoreCase).ToList();
        SplinePaths = _entries
            .Where(entry => Path.GetExtension(entry.Path).Equals(".spl", StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.Path.Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<RenderWareAssetFile> Assets { get; }
    public IReadOnlyList<string> SplinePaths { get; }
    public int DffCount => Assets.Count(asset => asset.Kind == RenderWareAssetKind.DffModel);
    public int RwsCount => Assets.Count(asset => asset.Kind == RenderWareAssetKind.RwsScene);

    public static RenderWareSceneArchive Load(string metPath) => new(metPath, METFileReader.ReadMETFile(metPath));

    public static string? GetStadiumScenePath(string folderName) =>
        StadiumScenePaths.TryGetValue(folderName, out string? path) ? path : null;

    public RenderWareAssetFile? FindStadiumScene(string folderName)
    {
        string? path = GetStadiumScenePath(folderName);
        return path == null ? null : Assets.FirstOrDefault(asset =>
            asset.Path.Replace('\\', '/').Equals(path, StringComparison.OrdinalIgnoreCase));
    }

    public RenderWareAssetFile? FindAmbientModel(string pathValue, string assetValue, string stadiumFolder)
    {
        string assetName = CleanFieldAssetValue(assetValue);
        if (assetName.Length == 0) return null;
        string preferredDirectory = "data/" + pathValue.Trim().TrimEnd(';').Replace('\\', '/').Trim('/');
        string preferredPath = (preferredDirectory + "/" + assetName).ToLowerInvariant();
        List<RenderWareAssetFile> candidates = Assets.Where(asset =>
                asset.Kind == RenderWareAssetKind.DffModel &&
                Path.GetFileName(asset.Path).Equals(assetName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return candidates.OrderByDescending(asset =>
        {
            string normalized = asset.Path.Replace('\\', '/');
            if (normalized.Equals(preferredPath, StringComparison.OrdinalIgnoreCase)) return 100_000;
            if (normalized.StartsWith(preferredDirectory + "/", StringComparison.OrdinalIgnoreCase)) return 90_000;
            if (normalized.StartsWith($"data/fields/{stadiumFolder}/", StringComparison.OrdinalIgnoreCase)) return 80_000;
            if (normalized.StartsWith("data/fields/commonambients/", StringComparison.OrdinalIgnoreCase)) return 70_000;
            return 0;
        }).FirstOrDefault();
    }

    public byte[]? ReadRawPath(string path)
    {
        FileEntry? entry = _entries.FirstOrDefault(candidate =>
            candidate.Path.Replace('\\', '/').Equals(path.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));
        return entry == null ? null : ReadEntry(entry);
    }

    public RenderWareScene LoadScene(RenderWareAssetFile asset)
    {
        RenderWareScene scene = RenderWareSceneParser.Parse(asset.Path, asset.Kind, ReadEntry(asset.Entry));
        HashSet<string> wanted = scene.Meshes.SelectMany(mesh => mesh.Materials)
            .Select(material => material.TextureName).Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (FileEntry textureEntry in _entries
                     .Where(entry => Path.GetExtension(entry.Path).Equals(".png", StringComparison.OrdinalIgnoreCase))
                     .Where(entry => wanted.Contains(Path.GetFileNameWithoutExtension(entry.Path)))
                     .GroupBy(entry => Path.GetFileNameWithoutExtension(entry.Path), StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.OrderByDescending(entry => TextureScore(entry.Path, asset.Path)).First()))
        {
            try
            {
                scene.AddTexture(Path.GetFileNameWithoutExtension(textureEntry.Path),
                    RenderWareTexture.Decode(textureEntry.Path, ReadEntry(textureEntry)));
            }
            catch (Exception exception) when (exception is ArgumentException or
                                               System.Runtime.InteropServices.ExternalException)
            {
                scene.Warnings.Add($"Could not decode {textureEntry.Path}.");
            }
        }
        return scene;
    }

    public byte[] ReadRaw(RenderWareAssetFile asset) => ReadEntry(asset.Entry);

    public static void ExportObj(RenderWareScene scene, string objPath)
    {
        string directory = Path.GetDirectoryName(objPath) ?? string.Empty;
        Directory.CreateDirectory(directory);
        string stem = Path.GetFileNameWithoutExtension(objPath);
        string mtlPath = Path.Combine(directory, stem + ".mtl");
        using StreamWriter obj = new(objPath, false, new UTF8Encoding(false));
        using StreamWriter mtl = new(mtlPath, false, new UTF8Encoding(false));
        obj.WriteLine($"# Exported from {scene.SourcePath}");
        obj.WriteLine($"mtllib {Path.GetFileName(mtlPath)}");
        int vertexBase = 1;
        int materialBase = 0;
        foreach (RenderWareSceneMesh mesh in scene.Meshes)
        {
            obj.WriteLine($"o {SafeName(mesh.Name)}");
            foreach (RenderWareSceneVertex vertex in mesh.Vertices)
                obj.WriteLine(FormattableString.Invariant($"v {vertex.Position.X:R} {vertex.Position.Y:R} {vertex.Position.Z:R}"));
            foreach (RenderWareSceneVertex vertex in mesh.Vertices)
                obj.WriteLine(FormattableString.Invariant($"vt {vertex.TextureCoordinate.X:R} {1F - vertex.TextureCoordinate.Y:R}"));
            foreach (RenderWareSceneVertex vertex in mesh.Vertices)
                obj.WriteLine(FormattableString.Invariant($"vn {vertex.Normal.X:R} {vertex.Normal.Y:R} {vertex.Normal.Z:R}"));
            for (int index = 0; index < mesh.Materials.Count; index++)
            {
                RenderWareMaterial material = mesh.Materials[index];
                mtl.WriteLine($"newmtl material_{materialBase + index:0000}");
                mtl.WriteLine(FormattableString.Invariant(
                    $"Kd {material.Color.R / 255F:R} {material.Color.G / 255F:R} {material.Color.B / 255F:R}"));
                mtl.WriteLine(FormattableString.Invariant($"d {material.Color.A / 255F:R}"));
                if (!string.IsNullOrWhiteSpace(material.TextureName))
                    mtl.WriteLine($"map_Kd {SafeName(material.TextureName)}.png");
                mtl.WriteLine();
            }
            int previousMaterial = -1;
            foreach (RenderWareTriangle triangle in mesh.Triangles)
            {
                if (triangle.MaterialIndex != previousMaterial)
                {
                    previousMaterial = triangle.MaterialIndex;
                    obj.WriteLine($"usemtl material_{materialBase + Math.Max(0, previousMaterial):0000}");
                }
                int a = vertexBase + triangle.First, b = vertexBase + triangle.Second, c = vertexBase + triangle.Third;
                obj.WriteLine($"f {a}/{a}/{a} {b}/{b}/{b} {c}/{c}/{c}");
            }
            vertexBase += mesh.Vertices.Count;
            materialBase += mesh.Materials.Count;
        }
    }

    public static void ExportTextures(RenderWareScene scene, string directory)
    {
        Directory.CreateDirectory(directory);
        foreach ((string name, RenderWareTexture texture) in scene.Textures)
        {
            using Bitmap bitmap = new(texture.Width, texture.Height,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            System.Drawing.Imaging.BitmapData locked = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try { System.Runtime.InteropServices.Marshal.Copy(texture.Pixels, 0, locked.Scan0, texture.Pixels.Length); }
            finally { bitmap.UnlockBits(locked); }
            bitmap.Save(Path.Combine(directory, SafeName(name) + ".png"), System.Drawing.Imaging.ImageFormat.Png);
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

    private static string GetCategory(string path)
    {
        string[] parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 1 ? parts[1] : "other";
    }

    private static int TextureScore(string texturePath, string assetPath)
    {
        string textureDirectory = Path.GetDirectoryName(texturePath.Replace('\\', '/')) ?? string.Empty;
        string modelDirectory = Path.GetDirectoryName(assetPath.Replace('\\', '/')) ?? string.Empty;
        if (textureDirectory.Equals(modelDirectory, StringComparison.OrdinalIgnoreCase)) return 100_000;
        string[] a = textureDirectory.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string[] b = modelDirectory.Split('/', StringSplitOptions.RemoveEmptyEntries);
        int common = 0;
        while (common < a.Length && common < b.Length && a[common].Equals(b[common], StringComparison.OrdinalIgnoreCase)) common++;
        return common * 1000;
    }

    private static string SafeName(string value)
    {
        StringBuilder result = new(value.Length);
        foreach (char c in value) result.Append(char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_');
        return result.Length == 0 ? "unnamed" : result.ToString();
    }

    private static string CleanFieldAssetValue(string value)
    {
        string clean = value.Split(';', 2)[0].Trim();
        int space = clean.IndexOfAny([' ', '\t']);
        return space < 0 ? clean : clean[..space];
    }
}

public sealed class RenderWareScene
{
    private readonly Dictionary<string, RenderWareTexture> _textures = new(StringComparer.OrdinalIgnoreCase);
    internal RenderWareScene(string sourcePath, RenderWareAssetKind kind,
        IReadOnlyList<RenderWareSceneMesh> meshes, IReadOnlyList<RenderWareChunkInfo> chunks,
        int planeSectors, int worldSectors, int embeddedClumps, IReadOnlyList<string> nativeTextures,
        List<string> warnings)
    {
        SourcePath = sourcePath; Kind = kind; Meshes = meshes; Chunks = chunks;
        PlaneSectorCount = planeSectors; WorldSectorCount = worldSectors;
        EmbeddedClumpCount = embeddedClumps; NativeTextureNames = nativeTextures; Warnings = warnings;
    }
    public string SourcePath { get; }
    public RenderWareAssetKind Kind { get; }
    public IReadOnlyList<RenderWareSceneMesh> Meshes { get; }
    public IReadOnlyList<RenderWareChunkInfo> Chunks { get; }
    public int PlaneSectorCount { get; }
    public int WorldSectorCount { get; }
    public int EmbeddedClumpCount { get; }
    public IReadOnlyList<string> NativeTextureNames { get; }
    public List<string> Warnings { get; }
    public IReadOnlyDictionary<string, RenderWareTexture> Textures => _textures;
    public int VertexCount => Meshes.Sum(mesh => mesh.Vertices.Count);
    public int TriangleCount => Meshes.Sum(mesh => mesh.Triangles.Count);
    public int MaterialCount => Meshes.Sum(mesh => mesh.Materials.Count);
    public int UniqueMaterialCount => Meshes.SelectMany(mesh => mesh.Materials)
        .DistinctBy(material => (material.TextureName?.ToUpperInvariant(), material.Color.ToArgb(),
            material.FilterMode, material.AddressU, material.AddressV)).Count();
    public void AddTexture(string name, RenderWareTexture texture) => _textures[name] = texture;
    public RenderWareTexture? ResolveTexture(RenderWareMaterial material) =>
        string.IsNullOrWhiteSpace(material.TextureName) ? null : _textures.GetValueOrDefault(material.TextureName);
}

public sealed record RenderWareSceneMesh(string Name, IReadOnlyList<RenderWareSceneVertex> Vertices,
    IReadOnlyList<RenderWareTriangle> Triangles, IReadOnlyList<RenderWareMaterial> Materials, string SourceType)
{
    public RenderWareWorldSectorSource? WorldSectorSource { get; init; }
    public RenderWareGeometrySource? GeometrySource { get; init; }
}
public sealed record RenderWareWorldSectorSource(int PositionDataOffset, int BoundsOffset);
public sealed record RenderWareGeometrySource(
    int PositionDataOffset, int BoundingSphereOffset, Matrix4x4 LocalToWorld)
{
    public RenderWareCollisionTreeSource? CollisionTreeSource { get; init; }
}
public sealed record RenderWareCollisionTreeSource(
    int BoundsOffset,
    int SplitDataOffset,
    int EntryMapOffset,
    uint Flags,
    int NumEntries,
    int NumSplits);
public readonly record struct RenderWareSceneVertex(Vector3 Position, Vector3 Normal,
    Vector2 TextureCoordinate, Color Color);
public sealed record RenderWareChunkInfo(uint Id, string Name, int Offset, int Length, uint Version);

internal static class RenderWareSceneParser
{
    private const uint Struct = 0x01, String = 0x02, Extension = 0x03, Texture = 0x06;
    private const uint Material = 0x07, MaterialList = 0x08, AtomicSector = 0x09;
    private const uint PlaneSector = 0x0A, World = 0x0B, FrameList = 0x0E, Geometry = 0x0F;
    private const uint Clump = 0x10, Atomic = 0x14, TextureNative = 0x15;
    private const uint TextureDictionary = 0x16, GeometryList = 0x1A, PiTextureDictionary = 0x23;
    private const uint CollisionTree = 0x2C, CollisionPlugin = 0x11D;
    private const uint NativeFlag = 0x01000000;

    public static RenderWareScene Parse(string sourcePath, RenderWareAssetKind kind, byte[] data)
    {
        List<RenderWareSceneMesh> meshes = new();
        List<RenderWareChunkInfo> chunks = new();
        List<string> warnings = new();
        HashSet<string> nativeTextures = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, RenderWareTexture> embeddedTextures = new(StringComparer.OrdinalIgnoreCase);
        int planeSectors = 0, worldSectors = 0, embeddedClumps = 0;
        for (int offset = 0; offset < data.Length; offset = ChunkEnd(data, offset))
        {
            uint id = U32(data, offset);
            chunks.Add(new RenderWareChunkInfo(id, ChunkName(id), offset, I32(data, offset + 4), U32(data, offset + 8)));
            try
            {
                if (id == Clump)
                {
                    meshes.AddRange(ParseClump(sourcePath, data, offset));
                    embeddedClumps++;
                }
                else if (id == World)
                {
                    WorldResult result = ParseWorld(sourcePath, data, offset);
                    meshes.AddRange(result.Meshes);
                    planeSectors += result.PlaneSectors;
                    worldSectors += result.WorldSectors;
                }
                else if (id == PiTextureDictionary)
                {
                    foreach (DecodedEmbeddedTexture texture in DecodePiTextureDictionary(data, offset, sourcePath))
                    {
                        nativeTextures.Add(texture.Name);
                        embeddedTextures[texture.Name] = texture.Texture;
                    }
                }
                else if (id == TextureDictionary)
                    CollectNativeTextureNames(data, offset, nativeTextures);
            }
            catch (InvalidDataException exception) { warnings.Add(exception.Message); }
        }
        if (meshes.Count == 0)
            warnings.Add("This asset has no renderable triangle geometry (it may contain cameras, particle emitters, or flyby markers only).");
        RenderWareScene scene = new(sourcePath, kind, meshes, chunks, planeSectors, worldSectors,
            kind == RenderWareAssetKind.RwsScene ? embeddedClumps : 0,
            nativeTextures.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList(), warnings);
        foreach ((string name, RenderWareTexture texture) in embeddedTextures)
            scene.AddTexture(name, texture);
        return scene;
    }

    private static IReadOnlyList<RenderWareSceneMesh> ParseClump(string sourcePath, ReadOnlySpan<byte> data, int clump)
    {
        int end = ChunkEnd(data, clump);
        int frameList = FindChild(data, clump + 12, end, FrameList);
        int geometryList = FindChild(data, clump + 12, end, GeometryList);
        if (geometryList < 0) throw new InvalidDataException($"'{sourcePath}' has a clump without a geometry list.");
        IReadOnlyList<Matrix4x4> frames = frameList < 0 ? Array.Empty<Matrix4x4>() : ParseFrames(data, frameList);
        List<GeometryData> geometries = new();
        int listEnd = ChunkEnd(data, geometryList);
        for (int child = geometryList + 12; child < listEnd; child = ChunkEnd(data, child))
            if (U32(data, child) == Geometry)
                geometries.Add(ParseGeometry(sourcePath, data, child));

        List<(int Frame, int Geometry)> atomics = new();
        for (int child = clump + 12; child < end; child = ChunkEnd(data, child))
        {
            if (U32(data, child) != Atomic) continue;
            int structure = child + 12;
            if (U32(data, structure) != Struct || I32(data, structure + 4) < 8) continue;
            atomics.Add((I32(data, structure + 12), I32(data, structure + 16)));
        }
        if (atomics.Count == 0)
            atomics.AddRange(Enumerable.Range(0, geometries.Count).Select(index => (0, index)));

        List<RenderWareSceneMesh> result = new();
        int atomicIndex = 0;
        foreach ((int frameIndex, int geometryIndex) in atomics)
        {
            if (geometryIndex < 0 || geometryIndex >= geometries.Count) continue;
            GeometryData geometry = geometries[geometryIndex];
            Matrix4x4 transform = frameIndex >= 0 && frameIndex < frames.Count ? frames[frameIndex] : Matrix4x4.Identity;
            List<RenderWareSceneVertex> vertices = geometry.Vertices.Select(vertex =>
            {
                Vector3 normal = Vector3.TransformNormal(vertex.Normal, transform);
                if (normal.LengthSquared() > 0.000001F) normal = Vector3.Normalize(normal);
                return new RenderWareSceneVertex(Vector3.Transform(vertex.Position, transform), normal,
                    vertex.TextureCoordinate, vertex.Color);
            }).ToList();
            result.Add(new RenderWareSceneMesh($"Clump atomic {atomicIndex++}", vertices,
                geometry.Triangles, geometry.Materials, "Clump")
            {
                GeometrySource = geometry.Source == null ? null : new RenderWareGeometrySource(
                    geometry.Source.PositionDataOffset, geometry.Source.BoundingSphereOffset, transform)
                {
                    CollisionTreeSource = geometry.Source.CollisionTreeSource
                }
            });
        }
        return result;
    }

    private static IReadOnlyList<Matrix4x4> ParseFrames(ReadOnlySpan<byte> data, int frameList)
    {
        int structure = frameList + 12;
        if (U32(data, structure) != Struct) return Array.Empty<Matrix4x4>();
        int payload = structure + 12;
        int count = I32(data, payload);
        if (count < 0 || count > 100_000) throw new InvalidDataException("Invalid RenderWare frame count.");
        Matrix4x4[] local = new Matrix4x4[count];
        int[] parents = new int[count];
        int offset = payload + 4;
        for (int index = 0; index < count; index++, offset += 56)
        {
            local[index] = new Matrix4x4(
                F32(data, offset), F32(data, offset + 4), F32(data, offset + 8), 0,
                F32(data, offset + 12), F32(data, offset + 16), F32(data, offset + 20), 0,
                F32(data, offset + 24), F32(data, offset + 28), F32(data, offset + 32), 0,
                F32(data, offset + 36), F32(data, offset + 40), F32(data, offset + 44), 1);
            parents[index] = I32(data, offset + 48);
        }
        Matrix4x4[] world = new Matrix4x4[count];
        bool[] complete = new bool[count];
        for (int index = 0; index < count; index++) Resolve(index, new HashSet<int>());
        return world;

        Matrix4x4 Resolve(int index, HashSet<int> stack)
        {
            if (complete[index]) return world[index];
            if (!stack.Add(index)) return local[index];
            int parent = parents[index];
            world[index] = parent >= 0 && parent < count ? local[index] * Resolve(parent, stack) : local[index];
            complete[index] = true;
            return world[index];
        }
    }

    private static GeometryData ParseGeometry(string sourcePath, ReadOnlySpan<byte> data, int geometry)
    {
        int end = ChunkEnd(data, geometry);
        int structure = geometry + 12;
        if (U32(data, structure) != Struct) throw new InvalidDataException($"'{sourcePath}' has invalid geometry.");
        int structureEnd = ChunkEnd(data, structure);
        int offset = structure + 12;
        uint flags = U32(data, offset);
        int triangleCount = I32(data, offset + 4), vertexCount = I32(data, offset + 8), morphCount = I32(data, offset + 12);
        if ((flags & NativeFlag) != 0) throw new InvalidDataException($"'{sourcePath}' uses native geometry that cannot be decoded yet.");
        if (triangleCount < 0 || vertexCount <= 0 || morphCount <= 0)
            throw new InvalidDataException($"'{sourcePath}' has invalid geometry counts.");
        offset += 16;
        Color[] colors = Enumerable.Repeat(Color.White, vertexCount).ToArray();
        if ((flags & 0x08) != 0)
            for (int vertex = 0; vertex < vertexCount; vertex++, offset += 4)
                colors[vertex] = Color.FromArgb(data[offset + 3], data[offset], data[offset + 1], data[offset + 2]);
        int texSets = (int)((flags >> 16) & 0xFF);
        if (texSets == 0) texSets = (flags & 0x80) != 0 ? 2 : (flags & 0x04) != 0 ? 1 : 0;
        Vector2[] uv = new Vector2[vertexCount];
        for (int set = 0; set < texSets; set++)
            for (int vertex = 0; vertex < vertexCount; vertex++, offset += 8)
                if (set == 0) uv[vertex] = new Vector2(F32(data, offset), F32(data, offset + 4));
        RenderWareTriangle[] triangles = new RenderWareTriangle[triangleCount];
        for (int index = 0; index < triangleCount; index++, offset += 8)
            triangles[index] = new RenderWareTriangle(U16(data, offset + 2), U16(data, offset),
                U16(data, offset + 6), U16(data, offset + 4));
        Vector3[]? positions = null, normals = null;
        GeometrySourceData? source = null;
        for (int morph = 0; morph < morphCount; morph++)
        {
            int boundingSphereOffset = offset;
            offset += 16;
            bool hasPositions = I32(data, offset) != 0, hasNormals = I32(data, offset + 4) != 0;
            offset += 8;
            int positionDataOffset = offset;
            Vector3[]? p = hasPositions ? ReadVectors(data, ref offset, vertexCount) : null;
            Vector3[]? n = hasNormals ? ReadVectors(data, ref offset, vertexCount) : null;
            if (positions == null && p != null)
                source = new GeometrySourceData(positionDataOffset, boundingSphereOffset);
            positions ??= p; normals ??= n;
        }
        if (positions == null || offset != structureEnd)
            throw new InvalidDataException($"'{sourcePath}' has malformed geometry arrays.");
        int materialList = FindChild(data, structureEnd, end, MaterialList);
        IReadOnlyList<RenderWareMaterial> materials = materialList >= 0
            ? ParseMaterials(sourcePath, data, materialList)
            : new[] { new RenderWareMaterial(null, Color.LightGray) };
        List<RenderWareSceneVertex> vertices = new(vertexCount);
        for (int vertex = 0; vertex < vertexCount; vertex++)
            vertices.Add(new RenderWareSceneVertex(positions[vertex], normals?[vertex] ?? Vector3.UnitY,
                uv[vertex], colors[vertex]));
        if (source != null)
            source = source with { CollisionTreeSource = ParseCollisionTreeSource(data, structureEnd, end) };
        return new GeometryData(vertices, triangles, materials, source);
    }

    private static RenderWareCollisionTreeSource? ParseCollisionTreeSource(
        ReadOnlySpan<byte> data,
        int geometryStructureEnd,
        int geometryEnd)
    {
        int extension = FindChild(data, geometryStructureEnd, geometryEnd, Extension);
        if (extension < 0) return null;
        int plugin = FindChild(data, extension + 12, ChunkEnd(data, extension), CollisionPlugin);
        if (plugin < 0 || I32(data, plugin + 4) < 4) return null;

        int payload = plugin + 12;
        uint collisionVersion = U32(data, payload);
        if (collisionVersion < 0x00036001) return null;
        int tree = payload + 4;
        if (tree + 12 > ChunkEnd(data, plugin) || U32(data, tree) != CollisionTree) return null;
        int treeEnd = ChunkEnd(data, tree);
        int treeStructure = tree + 12;
        if (treeStructure + 12 > treeEnd || U32(data, treeStructure) != Struct) return null;
        int structureEnd = ChunkEnd(data, treeStructure);
        int values = treeStructure + 12;
        if (values + 36 > structureEnd) return null;

        uint flags = U32(data, values);
        int numEntries = I32(data, values + 28);
        int numSplits = I32(data, values + 32);
        if (numEntries <= 0 || numEntries > ushort.MaxValue ||
            numSplits < 0 || numSplits > ushort.MaxValue)
            return null;

        int splitDataOffset = values + 36;
        long entryMapOffset = (long)splitDataOffset + numSplits * 16L;
        long expectedEnd = entryMapOffset + ((flags & 1) != 0 ? numEntries * 2L : 0L);
        if (expectedEnd > structureEnd) return null;
        return new RenderWareCollisionTreeSource(
            values + 4, splitDataOffset, (int)entryMapOffset, flags, numEntries, numSplits);
    }

    private static WorldResult ParseWorld(string sourcePath, byte[] data, int world)
    {
        int end = ChunkEnd(data, world);
        int structure = world + 12;
        if (U32(data, structure) != Struct || I32(data, structure + 4) < 40)
            throw new InvalidDataException($"'{sourcePath}' has an invalid World structure.");
        int payload = structure + 12;
        uint flags = U32(data, payload + 36);
        if ((flags & NativeFlag) != 0)
            throw new InvalidDataException($"'{sourcePath}' uses a native World that cannot be decoded yet.");
        int materialList = FindChild(data, ChunkEnd(data, structure), end, MaterialList);
        IReadOnlyList<RenderWareMaterial> materials = materialList >= 0
            ? ParseMaterials(sourcePath, data, materialList)
            : new[] { new RenderWareMaterial(null, Color.LightGray) };
        int root = materialList >= 0 ? ChunkEnd(data, materialList) : ChunkEnd(data, structure);
        List<RenderWareSceneMesh> meshes = new();
        int planeCount = 0, worldCount = 0;
        ParseSector(root);
        return new WorldResult(meshes, planeCount, worldCount);

        void ParseSector(int sector)
        {
            uint id = U32(data, sector);
            if (id == PlaneSector)
            {
                planeCount++;
                int planeStruct = sector + 12;
                int left = ChunkEnd(data, planeStruct);
                ParseSector(left);
                ParseSector(ChunkEnd(data, left));
                return;
            }
            if (id != AtomicSector)
                throw new InvalidDataException($"'{sourcePath}' has an unknown BSP sector 0x{id:X}.");
            int sectorNumber = worldCount++;
            int sectorStruct = sector + 12;
            int length = I32(data, sectorStruct + 4);
            int p = sectorStruct + 12;
            int materialBase = I32(data, p);
            int triangleCount = I32(data, p + 4), vertexCount = I32(data, p + 8);
            int texSets = (int)((flags >> 16) & 0xFF);
            if (texSets == 0) texSets = (flags & 0x80) != 0 ? 2 : (flags & 0x04) != 0 ? 1 : 0;
            int arraySize = checked(vertexCount * 12 +
                                    ((flags & 0x10) != 0 ? vertexCount * 4 : 0) +
                                    ((flags & 0x08) != 0 ? vertexCount * 4 : 0) +
                                    vertexCount * texSets * 8 + triangleCount * 8);
            int headerSize = length - arraySize;
            if (headerSize < 36 || headerSize > 128 || vertexCount < 0 || triangleCount < 0)
                throw new InvalidDataException($"'{sourcePath}' has malformed World sector arrays.");
            int offset = p + headerSize;
            int positionDataOffset = offset;
            Vector3[] positions = ReadVectors(data, ref offset, vertexCount);
            Vector3[] normals = Enumerable.Repeat(Vector3.UnitY, vertexCount).ToArray();
            if ((flags & 0x10) != 0)
                for (int vertex = 0; vertex < vertexCount; vertex++, offset += 4)
                {
                    Vector3 normal = new((sbyte)data[offset] / 127F, (sbyte)data[offset + 1] / 127F,
                        (sbyte)data[offset + 2] / 127F);
                    normals[vertex] = normal.LengthSquared() > 0.0001F ? Vector3.Normalize(normal) : Vector3.UnitY;
                }
            Color[] colors = Enumerable.Repeat(Color.White, vertexCount).ToArray();
            if ((flags & 0x08) != 0)
                for (int vertex = 0; vertex < vertexCount; vertex++, offset += 4)
                    colors[vertex] = Color.FromArgb(data[offset + 3], data[offset], data[offset + 1], data[offset + 2]);
            Vector2[] uv = new Vector2[vertexCount];
            for (int set = 0; set < texSets; set++)
                for (int vertex = 0; vertex < vertexCount; vertex++, offset += 8)
                    if (set == 0) uv[vertex] = new Vector2(F32(data, offset), F32(data, offset + 4));
            RenderWareTriangle[] triangles = new RenderWareTriangle[triangleCount];
            for (int index = 0; index < triangleCount; index++, offset += 8)
            {
                int material = materialBase + U16(data, offset + 6);
                triangles[index] = new RenderWareTriangle(U16(data, offset), U16(data, offset + 2),
                    U16(data, offset + 4), material);
            }
            List<RenderWareSceneVertex> vertices = new(vertexCount);
            for (int vertex = 0; vertex < vertexCount; vertex++)
                vertices.Add(new RenderWareSceneVertex(positions[vertex], normals[vertex], uv[vertex], colors[vertex]));
            meshes.Add(new RenderWareSceneMesh($"World sector {sectorNumber}", vertices, triangles, materials, "World sector")
            {
                WorldSectorSource = new RenderWareWorldSectorSource(positionDataOffset, p + 12)
            });
        }
    }

    private static IReadOnlyList<RenderWareMaterial> ParseMaterials(string sourcePath, ReadOnlySpan<byte> data, int list)
    {
        int end = ChunkEnd(data, list);
        int structure = list + 12;
        if (U32(data, structure) != Struct)
            throw new InvalidDataException($"'{sourcePath}' has an invalid material list.");
        int count = I32(data, structure + 12);
        if (count < 0 || count > 100_000 || I32(data, structure + 4) < 4 + count * 4)
            throw new InvalidDataException($"'{sourcePath}' has an invalid material count.");
        List<int> materialChunks = new();
        for (int child = ChunkEnd(data, structure); child < end; child = ChunkEnd(data, child))
            if (U32(data, child) == Material) materialChunks.Add(child);
        int nextChunk = 0;
        List<RenderWareMaterial> materials = new(Math.Max(0, count));
        for (int slot = 0; slot < count; slot++)
        {
            int shared = I32(data, structure + 16 + slot * 4);
            if (shared >= 0 && shared < materials.Count)
            {
                materials.Add(materials[shared]);
                continue;
            }
            materials.Add(nextChunk < materialChunks.Count
                ? ParseMaterial(data, materialChunks[nextChunk++])
                : new RenderWareMaterial(null, Color.LightGray));
        }
        return materials;
    }

    private static RenderWareMaterial ParseMaterial(ReadOnlySpan<byte> data, int material)
    {
        int materialEnd = ChunkEnd(data, material), materialStruct = material + 12, p = materialStruct + 12;
        Color color = Color.FromArgb(data[p + 7], data[p + 4], data[p + 5], data[p + 6]);
        int texture = FindChild(data, ChunkEnd(data, materialStruct), materialEnd, Texture);
        if (texture < 0) return new RenderWareMaterial(null, color);
        int textureStruct = texture + 12;
        uint sampling = I32(data, textureStruct + 4) >= 4 ? U32(data, textureStruct + 12) : 0;
        int nameChunk = FindChild(data, ChunkEnd(data, textureStruct), ChunkEnd(data, texture), String);
        string? name = nameChunk < 0 ? null :
            ReadString(data.Slice(nameChunk + 12, I32(data, nameChunk + 4))).Trim();
        return new RenderWareMaterial(name, color, (byte)sampling,
            (byte)((sampling >> 8) & 0x0F), (byte)((sampling >> 12) & 0x0F));
    }

    private static IReadOnlyList<DecodedEmbeddedTexture> DecodePiTextureDictionary(
        ReadOnlySpan<byte> data, int dictionary, string sourcePath)
    {
        int end = ChunkEnd(data, dictionary), offset = dictionary + 12;
        if (offset + 4 > end) throw new InvalidDataException($"'{sourcePath}' has a truncated texture dictionary.");
        uint dictionaryHeader = U32(data, offset);
        int textureCount = (int)(dictionaryHeader & 0xFFFF);
        if (textureCount < 0 || textureCount > 10_000)
            throw new InvalidDataException($"'{sourcePath}' has an invalid embedded texture count.");
        offset += 4;
        List<DecodedEmbeddedTexture> result = new(textureCount);
        for (int textureIndex = 0; textureIndex < textureCount; textureIndex++)
        {
            if (offset + 4 > end) throw new InvalidDataException($"'{sourcePath}' has a truncated texture record.");
            int mipCount = I32(data, offset);
            if (mipCount <= 0 || mipCount > 32)
                throw new InvalidDataException($"'{sourcePath}' has an invalid embedded mip count.");
            offset += 4;
            DecodedRwImage? baseImage = null;
            for (int mip = 0; mip < mipCount; mip++)
            {
                if (offset >= end || U32(data, offset) != 0x18)
                    throw new InvalidDataException($"'{sourcePath}' has a malformed embedded image.");
                if (mip == 0) baseImage = DecodeRwImage(data, offset, sourcePath);
                offset = ChunkEnd(data, offset);
            }
            if (offset >= end || U32(data, offset) != Texture)
                throw new InvalidDataException($"'{sourcePath}' has an embedded image without texture metadata.");
            int textureEnd = ChunkEnd(data, offset);
            int textureStruct = offset + 12;
            int nameChunk = FindChild(data, ChunkEnd(data, textureStruct), textureEnd, String);
            string name = nameChunk < 0 ? string.Empty :
                ReadString(data.Slice(nameChunk + 12, I32(data, nameChunk + 4))).Trim();
            if (name.Length > 0 && baseImage != null)
            {
                string embeddedPath = $"{sourcePath} :: embedded/{name}";
                result.Add(new DecodedEmbeddedTexture(name, RenderWareTexture.FromArgb(embeddedPath,
                    baseImage.Width, baseImage.Height, baseImage.Pixels)));
            }
            offset = textureEnd;
        }
        if (offset != end)
            throw new InvalidDataException($"'{sourcePath}' has trailing data in its embedded texture dictionary.");
        return result;
    }

    private static DecodedRwImage DecodeRwImage(ReadOnlySpan<byte> data, int image, string sourcePath)
    {
        int imageEnd = ChunkEnd(data, image), structure = image + 12;
        if (U32(data, structure) != Struct || I32(data, structure + 4) < 16)
            throw new InvalidDataException($"'{sourcePath}' has an invalid embedded RwImage.");
        int p = structure + 12;
        int width = I32(data, p), height = I32(data, p + 4), depth = I32(data, p + 8), stride = I32(data, p + 12);
        if (width <= 0 || height <= 0 || width > 8192 || height > 8192 || stride <= 0)
            throw new InvalidDataException($"'{sourcePath}' has invalid embedded image dimensions.");
        int pixelsOffset = ChunkEnd(data, structure);
        int pixelBytes = checked(stride * height);
        int paletteEntries = depth == 4 ? 16 : depth == 8 ? 256 : 0;
        if (pixelsOffset + pixelBytes + paletteEntries * 4 > imageEnd || depth is not (4 or 8 or 24 or 32))
            throw new InvalidDataException($"'{sourcePath}' uses an unsupported embedded image layout.");
        int paletteOffset = pixelsOffset + pixelBytes;
        int[] pixels = new int[checked(width * height)];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int source = pixelsOffset + y * stride;
                if (depth == 4)
                {
                    byte packed = data[source + x / 2];
                    int index = (x & 1) == 0 ? packed & 0x0F : packed >> 4;
                    pixels[y * width + x] = ReadRgba(data, paletteOffset + index * 4);
                }
                else if (depth == 8)
                    pixels[y * width + x] = ReadRgba(data, paletteOffset + data[source + x] * 4);
                else
                    pixels[y * width + x] = ReadRgba(data, source + x * (depth / 8), depth == 32);
            }
        return new DecodedRwImage(width, height, pixels);
    }

    private static int ReadRgba(ReadOnlySpan<byte> data, int offset, bool hasAlpha = true)
    {
        int alpha = hasAlpha ? data[offset + 3] : 255;
        return (alpha << 24) | (data[offset] << 16) | (data[offset + 1] << 8) | data[offset + 2];
    }

    private static void CollectNativeTextureNames(ReadOnlySpan<byte> data, int dictionary, ISet<string> result)
    {
        int end = ChunkEnd(data, dictionary);
        // Backyard Baseball's 0x23 dictionaries contain a platform-specific PS2 raster table,
        // not the normal child-chunk form used by 0x16 texture dictionaries. Material names are
        // still recovered from the World/Clump material lists; leave the swizzled raster table
        // untouched until its GS layout is decoded.
        if (dictionary + 24 > end || U32(data, dictionary + 12) != Struct) return;
        for (int child = dictionary + 12; child < end; child = ChunkEnd(data, child))
        {
            if (U32(data, child) != TextureNative) continue;
            int nativeEnd = ChunkEnd(data, child);
            for (int item = child + 12; item < nativeEnd; item = ChunkEnd(data, item))
            {
                if (U32(data, item) != Struct) continue;
                foreach (string candidate in ExtractAsciiStrings(data.Slice(item + 12, I32(data, item + 4))))
                    if (candidate.Length is >= 3 and <= 64 && !candidate.Contains(' ')) result.Add(candidate);
                break;
            }
        }
    }

    private static IEnumerable<string> ExtractAsciiStrings(ReadOnlySpan<byte> data)
    {
        List<string> result = new();
        int start = -1;
        for (int index = 0; index <= data.Length; index++)
        {
            bool printable = index < data.Length && data[index] is >= 0x21 and <= 0x7E;
            if (printable && start < 0) start = index;
            if (!printable && start >= 0)
            {
                if (index - start >= 3) result.Add(Encoding.ASCII.GetString(data.Slice(start, index - start)));
                start = -1;
            }
        }
        return result;
    }

    private static Vector3[] ReadVectors(ReadOnlySpan<byte> data, ref int offset, int count)
    {
        Vector3[] result = new Vector3[count];
        for (int index = 0; index < count; index++, offset += 12)
            result[index] = new Vector3(F32(data, offset), F32(data, offset + 4), F32(data, offset + 8));
        return result;
    }

    private static int FindChild(ReadOnlySpan<byte> data, int start, int end, uint id)
    {
        for (int offset = start; offset < end; offset = ChunkEnd(data, offset))
            if (U32(data, offset) == id) return offset;
        return -1;
    }

    private static int ChunkEnd(ReadOnlySpan<byte> data, int offset)
    {
        if (offset < 0 || offset + 12 > data.Length)
            throw new InvalidDataException("RenderWare chunk header is truncated.");
        int length = I32(data, offset + 4);
        long end = (long)offset + 12 + length;
        if (length < 0 || end > data.Length)
            throw new InvalidDataException("RenderWare chunk extends beyond its file.");
        return (int)end;
    }

    private static string ChunkName(uint id) => id switch
    {
        Struct => "Struct",
        Extension => "Extension",
        MaterialList => "Material list",
        AtomicSector => "World sector",
        PlaneSector => "BSP plane",
        World => "World",
        FrameList => "Frame list",
        Geometry => "Geometry",
        Clump => "Clump",
        Atomic => "Atomic",
        GeometryList => "Geometry list",
        TextureDictionary => "Texture dictionary",
        PiTextureDictionary => "Platform texture dictionary",
        0x1B => "Animation",
        0x24 => "Table of contents",
        0x29 => "Chunk group start",
        0x2A => "Chunk group end",
        0x2B => "UV animation dictionary",
        _ => $"Chunk 0x{id:X}"
    };

    private static string ReadString(ReadOnlySpan<byte> data)
    {
        int zero = data.IndexOf((byte)0);
        return Encoding.ASCII.GetString(zero >= 0 ? data[..zero] : data);
    }

    private static uint U32(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
    private static ushort U16(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
    private static int I32(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4));
    private static float F32(ReadOnlySpan<byte> data, int offset) => BitConverter.Int32BitsToSingle(I32(data, offset));

    private sealed record GeometryData(IReadOnlyList<RenderWareSceneVertex> Vertices,
        IReadOnlyList<RenderWareTriangle> Triangles, IReadOnlyList<RenderWareMaterial> Materials,
        GeometrySourceData? Source);
    private sealed record GeometrySourceData(int PositionDataOffset, int BoundingSphereOffset)
    {
        public RenderWareCollisionTreeSource? CollisionTreeSource { get; init; }
    }
    private sealed record WorldResult(IReadOnlyList<RenderWareSceneMesh> Meshes, int PlaneSectors, int WorldSectors);
    private sealed record DecodedEmbeddedTexture(string Name, RenderWareTexture Texture);
    private sealed record DecodedRwImage(int Width, int Height, int[] Pixels);
}
