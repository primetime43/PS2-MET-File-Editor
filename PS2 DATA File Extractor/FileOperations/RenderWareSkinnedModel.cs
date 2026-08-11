using System.Buffers.Binary;
using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.InteropServices;

namespace PS2_DATA_File_Extractor.FileOperations;

public sealed class RenderWareSkinnedModel
{
    private const uint StructChunk = 0x01;
    private const uint StringChunk = 0x02;
    private const uint ExtensionChunk = 0x03;
    private const uint TextureChunk = 0x06;
    private const uint MaterialChunk = 0x07;
    private const uint MaterialListChunk = 0x08;
    private const uint GeometryChunk = 0x0F;
    private const uint GeometryListChunk = 0x1A;
    private const uint SkinPlugin = 0x116;
    private const uint NativeGeometryFlag = 0x01000000;

    private readonly Dictionary<string, RenderWareTexture> _textures =
        new(StringComparer.OrdinalIgnoreCase);

    private RenderWareSkinnedModel(
        string sourcePath,
        IReadOnlyList<RenderWareSkinnedMesh> meshes)
    {
        SourcePath = sourcePath;
        Meshes = meshes;
    }

    public string SourcePath { get; }
    public IReadOnlyList<RenderWareSkinnedMesh> Meshes { get; }
    public int VertexCount => Meshes.Sum(mesh => mesh.Vertices.Count);
    public int TriangleCount => Meshes.Sum(mesh => mesh.Triangles.Count);
    public IReadOnlyDictionary<string, RenderWareTexture> Textures => _textures;

    public static RenderWareSkinnedModel Parse(string sourcePath, ReadOnlySpan<byte> data)
    {
        int clumpEnd = ChunkEnd(data, 0);
        int geometryList = FindChild(data, 12, clumpEnd, GeometryListChunk);
        if (geometryList < 0)
            throw new InvalidDataException($"'{sourcePath}' has no RenderWare geometry list.");
        int listEnd = ChunkEnd(data, geometryList);
        List<RenderWareSkinnedMesh> meshes = new();
        for (int child = geometryList + 12; child < listEnd; child = ChunkEnd(data, child))
        {
            if (ReadUInt32(data, child) == GeometryChunk)
            {
                try
                {
                    meshes.Add(ParseGeometry(sourcePath, data, child));
                }
                catch (InvalidDataException)
                {
                    // Some player DFFs mix their skinned body with small rigid accessory atomics.
                    // Render every valid skinned geometry and leave unsupported rigid pieces out.
                }
            }
        }
        if (meshes.Count == 0)
            throw new InvalidDataException($"'{sourcePath}' has no supported skinned geometry.");
        return new RenderWareSkinnedModel(sourcePath, meshes);
    }

    public void AddTexture(string name, RenderWareTexture texture) => _textures[name] = texture;

    public void ReplaceTextureSource(string sourcePath, RenderWareTexture replacement)
    {
        foreach (string key in _textures
                     .Where(pair => pair.Value.SourcePath.Equals(sourcePath,
                         StringComparison.OrdinalIgnoreCase))
                     .Select(pair => pair.Key)
                     .ToList())
            _textures[key] = replacement;
    }

    public RenderWareTexture? ResolveTexture(
        RenderWareMaterial material,
        FacialEventFile? facialEvent,
        double timeSeconds)
    {
        string? name = material.TextureName;
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (facialEvent != null)
        {
            string? eventClass = name.Contains("_eyes_tx", StringComparison.OrdinalIgnoreCase)
                ? "CLASS_EYES"
                : name.Contains("_mouth_tx", StringComparison.OrdinalIgnoreCase)
                    ? "CLASS_MOUTH"
                    : null;
            FacialEvent? active = eventClass == null
                ? null
                : facialEvent.GetActiveEvent(eventClass, timeSeconds);
            if (active != null && int.TryParse(active.EventType, out int pose) && pose > 0)
            {
                int dot = name.LastIndexOf('.');
                string stem = dot >= 0 ? name[..dot] : name;
                string variant = $"{stem}.{pose:000}";
                if (_textures.TryGetValue(variant, out RenderWareTexture? selected)) return selected;
            }
        }
        return _textures.GetValueOrDefault(name);
    }

    public IReadOnlyList<RenderWareDeformedMesh> Deform(
        RenderWareAnimationBinding binding,
        RenderWareAnimationFile animation,
        float timeSeconds)
    {
        IReadOnlyList<Matrix4x4> world = binding.SampleWorldMatrices(animation, timeSeconds);
        List<RenderWareDeformedMesh> result = new(Meshes.Count);
        foreach (RenderWareSkinnedMesh mesh in Meshes)
        {
            if (mesh.InverseBindMatrices.Count != world.Count)
                throw new InvalidDataException(
                    $"'{SourcePath}' has {mesh.InverseBindMatrices.Count} skin bones but the animation has {world.Count} tracks.");
            Matrix4x4[] skinMatrices = new Matrix4x4[world.Count];
            for (int bone = 0; bone < world.Count; bone++)
                skinMatrices[bone] = mesh.InverseBindMatrices[bone] * world[bone];
            Vector3[] positions = new Vector3[mesh.Vertices.Count];
            for (int vertexIndex = 0; vertexIndex < mesh.Vertices.Count; vertexIndex++)
            {
                RenderWareSkinnedVertex vertex = mesh.Vertices[vertexIndex];
                Vector3 position = Vector3.Zero;
                float total = 0;
                ApplyWeight(vertex.Bone0, vertex.Weight0);
                ApplyWeight(vertex.Bone1, vertex.Weight1);
                ApplyWeight(vertex.Bone2, vertex.Weight2);
                ApplyWeight(vertex.Bone3, vertex.Weight3);
                positions[vertexIndex] = total > 0.000001F
                    ? position / total
                    : vertex.Position;

                void ApplyWeight(byte bone, float weight)
                {
                    if (weight <= 0 || bone >= skinMatrices.Length) return;
                    position += Vector3.Transform(vertex.Position, skinMatrices[bone]) * weight;
                    total += weight;
                }
            }
            result.Add(new RenderWareDeformedMesh(mesh, positions));
        }
        return result;
    }

    private static RenderWareSkinnedMesh ParseGeometry(
        string sourcePath, ReadOnlySpan<byte> data, int geometryOffset)
    {
        int geometryEnd = ChunkEnd(data, geometryOffset);
        int structure = geometryOffset + 12;
        if (ReadUInt32(data, structure) != StructChunk)
            throw new InvalidDataException($"'{sourcePath}' has an invalid geometry structure.");
        int structureEnd = ChunkEnd(data, structure);
        int offset = structure + 12;
        uint flags = ReadUInt32(data, offset);
        int triangleCount = ReadInt32(data, offset + 4);
        int vertexCount = ReadInt32(data, offset + 8);
        int morphCount = ReadInt32(data, offset + 12);
        if ((flags & NativeGeometryFlag) != 0)
            throw new InvalidDataException($"'{sourcePath}' uses unsupported native geometry.");
        if (vertexCount <= 0 || triangleCount <= 0 || morphCount <= 0)
            throw new InvalidDataException($"'{sourcePath}' has invalid geometry counts.");
        offset += 16;

        if ((flags & 0x08) != 0) offset += checked(vertexCount * 4);
        int texCoordSets = (int)((flags >> 16) & 0xFF);
        if (texCoordSets == 0)
            texCoordSets = (flags & 0x80) != 0 ? 2 : (flags & 0x04) != 0 ? 1 : 0;
        Vector2[] texCoords = new Vector2[vertexCount];
        for (int set = 0; set < texCoordSets; set++)
        {
            for (int vertex = 0; vertex < vertexCount; vertex++)
            {
                float u = ReadSingle(data, offset);
                float v = ReadSingle(data, offset + 4);
                if (set == 0) texCoords[vertex] = new Vector2(u, v);
                offset += 8;
            }
        }

        RenderWareTriangle[] triangles = new RenderWareTriangle[triangleCount];
        for (int triangle = 0; triangle < triangleCount; triangle++)
        {
            int second = ReadUInt16(data, offset);
            int first = ReadUInt16(data, offset + 2);
            int material = ReadUInt16(data, offset + 4);
            int third = ReadUInt16(data, offset + 6);
            if (first >= vertexCount || second >= vertexCount || third >= vertexCount)
                throw new InvalidDataException($"'{sourcePath}' has an out-of-range triangle index.");
            triangles[triangle] = new RenderWareTriangle(first, second, third, material);
            offset += 8;
        }

        Vector3[]? positions = null;
        Vector3[]? normals = null;
        for (int morph = 0; morph < morphCount; morph++)
        {
            offset += 16;
            bool hasPositions = ReadInt32(data, offset) != 0;
            bool hasNormals = ReadInt32(data, offset + 4) != 0;
            offset += 8;
            Vector3[]? morphPositions = hasPositions ? ReadVectors(data, ref offset, vertexCount) : null;
            Vector3[]? morphNormals = hasNormals ? ReadVectors(data, ref offset, vertexCount) : null;
            positions ??= morphPositions;
            normals ??= morphNormals;
        }
        if (positions == null || offset != structureEnd)
            throw new InvalidDataException($"'{sourcePath}' has invalid morph-target geometry.");

        int materialList = FindChild(data, structureEnd, geometryEnd, MaterialListChunk);
        int extension = FindChild(data, structureEnd, geometryEnd, ExtensionChunk);
        if (materialList < 0 || extension < 0)
            throw new InvalidDataException($"'{sourcePath}' is missing materials or skin data.");
        IReadOnlyList<RenderWareMaterial> materials = ParseMaterials(sourcePath, data, materialList);
        SkinData skin = ParseSkin(sourcePath, data, extension, vertexCount);
        List<RenderWareSkinnedVertex> vertices = new(vertexCount);
        for (int vertex = 0; vertex < vertexCount; vertex++)
        {
            int indexOffset = vertex * 4;
            vertices.Add(new RenderWareSkinnedVertex(
                positions[vertex], normals?[vertex] ?? Vector3.UnitY, texCoords[vertex],
                skin.Indices[indexOffset], skin.Indices[indexOffset + 1],
                skin.Indices[indexOffset + 2], skin.Indices[indexOffset + 3],
                skin.Weights[indexOffset], skin.Weights[indexOffset + 1],
                skin.Weights[indexOffset + 2], skin.Weights[indexOffset + 3]));
        }
        return new RenderWareSkinnedMesh(vertices, triangles, materials, skin.InverseBindMatrices);
    }

    private static IReadOnlyList<RenderWareMaterial> ParseMaterials(
        string sourcePath, ReadOnlySpan<byte> data, int listOffset)
    {
        int listEnd = ChunkEnd(data, listOffset);
        int structure = listOffset + 12;
        if (ReadUInt32(data, structure) != StructChunk)
            throw new InvalidDataException($"'{sourcePath}' has an invalid material list.");
        int count = ReadInt32(data, structure + 12);
        List<RenderWareMaterial> materials = new(count);
        for (int child = ChunkEnd(data, structure); child < listEnd; child = ChunkEnd(data, child))
        {
            if (ReadUInt32(data, child) != MaterialChunk) continue;
            int materialEnd = ChunkEnd(data, child);
            int materialStruct = child + 12;
            if (ReadUInt32(data, materialStruct) != StructChunk) continue;
            int payload = materialStruct + 12;
            Color color = Color.FromArgb(data[payload + 7], data[payload + 4],
                data[payload + 5], data[payload + 6]);
            int texture = FindChild(data, ChunkEnd(data, materialStruct), materialEnd, TextureChunk);
            string? textureName = null;
            if (texture >= 0)
            {
                int textureStruct = texture + 12;
                int nameChunk = FindChild(data, ChunkEnd(data, textureStruct),
                    ChunkEnd(data, texture), StringChunk);
                if (nameChunk >= 0)
                    textureName = ReadString(data.Slice(nameChunk + 12,
                        ReadInt32(data, nameChunk + 4))).Trim();
            }
            materials.Add(new RenderWareMaterial(textureName, color));
        }
        if (materials.Count != count)
            throw new InvalidDataException($"'{sourcePath}' has an incomplete material list.");
        return materials;
    }

    private static SkinData ParseSkin(
        string sourcePath, ReadOnlySpan<byte> data, int extensionOffset, int vertexCount)
    {
        int extensionEnd = ChunkEnd(data, extensionOffset);
        int skin = FindChild(data, extensionOffset + 12, extensionEnd, SkinPlugin);
        if (skin < 0) throw new InvalidDataException($"'{sourcePath}' has no RenderWare skin plugin.");
        int skinEnd = ChunkEnd(data, skin);
        int offset = skin + 12;
        int boneCount = data[offset];
        int usedBoneCount = data[offset + 1];
        if (boneCount <= 0 || usedBoneCount < 0 || usedBoneCount > boneCount)
            throw new InvalidDataException($"'{sourcePath}' has invalid skin bone counts.");
        offset += 4 + usedBoneCount;
        int indexCount = checked(vertexCount * 4);
        if (offset + indexCount > skinEnd)
            throw new InvalidDataException($"'{sourcePath}' has truncated skin indices.");
        byte[] indices = data.Slice(offset, indexCount).ToArray();
        offset += indexCount;
        float[] weights = new float[indexCount];
        for (int index = 0; index < indexCount; index++, offset += 4)
            weights[index] = ReadSingle(data, offset);
        Matrix4x4[] matrices = new Matrix4x4[boneCount];
        for (int bone = 0; bone < boneCount; bone++, offset += 64)
            matrices[bone] = ReadMatrix(data, offset);
        if (offset > skinEnd)
            throw new InvalidDataException($"'{sourcePath}' has truncated inverse-bind matrices.");
        return new SkinData(indices, weights, matrices);
    }

    private static Matrix4x4 ReadMatrix(ReadOnlySpan<byte> data, int offset) => new(
        ReadSingle(data, offset), ReadSingle(data, offset + 4), ReadSingle(data, offset + 8), 0,
        ReadSingle(data, offset + 16), ReadSingle(data, offset + 20), ReadSingle(data, offset + 24), 0,
        ReadSingle(data, offset + 32), ReadSingle(data, offset + 36), ReadSingle(data, offset + 40), 0,
        ReadSingle(data, offset + 48), ReadSingle(data, offset + 52), ReadSingle(data, offset + 56), 1);

    private static Vector3[] ReadVectors(ReadOnlySpan<byte> data, ref int offset, int count)
    {
        Vector3[] result = new Vector3[count];
        for (int index = 0; index < count; index++, offset += 12)
            result[index] = new Vector3(ReadSingle(data, offset),
                ReadSingle(data, offset + 4), ReadSingle(data, offset + 8));
        return result;
    }

    private static int FindChild(ReadOnlySpan<byte> data, int start, int end, uint id)
    {
        for (int offset = start; offset < end; offset = ChunkEnd(data, offset))
            if (ReadUInt32(data, offset) == id) return offset;
        return -1;
    }

    private static int ChunkEnd(ReadOnlySpan<byte> data, int offset)
    {
        if (offset < 0 || offset + 12 > data.Length)
            throw new InvalidDataException("RenderWare chunk header is outside the DFF payload.");
        int length = ReadInt32(data, offset + 4);
        long end = (long)offset + 12 + length;
        if (length < 0 || end > data.Length)
            throw new InvalidDataException("RenderWare chunk extends beyond the DFF payload.");
        return (int)end;
    }

    private static string ReadString(ReadOnlySpan<byte> data)
    {
        int zero = data.IndexOf((byte)0);
        return System.Text.Encoding.ASCII.GetString(zero >= 0 ? data[..zero] : data);
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
    private static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
    private static int ReadInt32(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4));
    private static float ReadSingle(ReadOnlySpan<byte> data, int offset) =>
        BitConverter.Int32BitsToSingle(ReadInt32(data, offset));

    private sealed record SkinData(byte[] Indices, float[] Weights, Matrix4x4[] InverseBindMatrices);
}

public sealed record RenderWareSkinnedMesh(
    IReadOnlyList<RenderWareSkinnedVertex> Vertices,
    IReadOnlyList<RenderWareTriangle> Triangles,
    IReadOnlyList<RenderWareMaterial> Materials,
    IReadOnlyList<Matrix4x4> InverseBindMatrices);

public sealed record RenderWareDeformedMesh(
    RenderWareSkinnedMesh Source,
    IReadOnlyList<Vector3> Positions);

public readonly record struct RenderWareSkinnedVertex(
    Vector3 Position,
    Vector3 Normal,
    Vector2 TextureCoordinate,
    byte Bone0,
    byte Bone1,
    byte Bone2,
    byte Bone3,
    float Weight0,
    float Weight1,
    float Weight2,
    float Weight3);

public readonly record struct RenderWareTriangle(int First, int Second, int Third, int MaterialIndex);

public sealed record RenderWareMaterial(string? TextureName, Color Color);

public sealed class RenderWareTexture
{
    private RenderWareTexture(string sourcePath, int width, int height, int[] pixels)
    {
        SourcePath = sourcePath;
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public string SourcePath { get; }
    public int Width { get; }
    public int Height { get; }
    public int[] Pixels { get; }

    public static RenderWareTexture Decode(ReadOnlySpan<byte> png) => Decode(string.Empty, png);

    public static RenderWareTexture Decode(string sourcePath, ReadOnlySpan<byte> png)
    {
        using MemoryStream stream = new(png.ToArray(), writable: false);
        using Image source = Image.FromStream(stream);
        using Bitmap bitmap = new(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(bitmap))
            graphics.DrawImage(source, 0, 0, bitmap.Width, bitmap.Height);
        Rectangle rectangle = new(0, 0, bitmap.Width, bitmap.Height);
        BitmapData locked = bitmap.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int[] pixels = new int[bitmap.Width * bitmap.Height];
            Marshal.Copy(locked.Scan0, pixels, 0, pixels.Length);
            return new RenderWareTexture(sourcePath, bitmap.Width, bitmap.Height, pixels);
        }
        finally
        {
            bitmap.UnlockBits(locked);
        }
    }
}
