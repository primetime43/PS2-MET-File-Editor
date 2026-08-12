using System.Buffers.Binary;
using System.Numerics;

namespace PS2_DATA_File_Extractor.FileOperations;

/// <summary>
/// Applies a fixed-size transform to the isolated RenderWare clump whose material is used as the
/// stadium's home-run collision surface. The original RWS layout and every non-HR byte are retained.
/// </summary>
public sealed class StadiumHomeRunBoundaryDocument
{
    private readonly byte[] _originalData;
    private readonly RenderWareScene _originalScene;
    private readonly List<TargetMesh> _targets;
    private byte[] _currentData;

    private StadiumHomeRunBoundaryDocument(
        RenderWareScene scene,
        byte[] rawData,
        string materialTag,
        List<TargetMesh> targets)
    {
        _originalScene = scene;
        _originalData = rawData.ToArray();
        _currentData = rawData.ToArray();
        _targets = targets;
        SourcePath = scene.SourcePath;
        MaterialTag = materialTag;
        OriginalBoundary = StadiumHomeRunAnalyzer.AnalyzeBoundary(scene, materialTag);
        PreviewScene = scene;
    }

    public string SourcePath { get; }
    public string MaterialTag { get; }
    public StadiumHomeRunBoundary OriginalBoundary { get; }
    public StadiumHomeRunBoundary CurrentBoundary =>
        StadiumHomeRunAnalyzer.AnalyzeBoundary(PreviewScene, MaterialTag);
    public RenderWareScene PreviewScene { get; private set; }
    public Vector3 Offset { get; private set; }
    public Vector3 Scale { get; private set; } = Vector3.One;
    public int ChangedVertexCount => _targets.Sum(target => target.VertexIndices.Count);
    public bool IsChanged => Offset != Vector3.Zero || Scale != Vector3.One;

    public static StadiumHomeRunBoundaryDocument Create(
        RenderWareScene scene,
        byte[] rawData,
        string materialTag)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(rawData);
        ArgumentException.ThrowIfNullOrWhiteSpace(materialTag);
        if (scene.Kind != RenderWareAssetKind.RwsScene)
            throw new InvalidDataException("Home-run boundaries can only be edited inside RWS stadium scenes.");

        List<TargetMesh> targets = [];
        HashSet<int> usedGeometryOffsets = [];
        for (int meshIndex = 0; meshIndex < scene.Meshes.Count; meshIndex++)
        {
            RenderWareSceneMesh mesh = scene.Meshes[meshIndex];
            HashSet<int> selected = [];
            HashSet<int> other = [];
            foreach (RenderWareTriangle triangle in mesh.Triangles)
            {
                bool matches = triangle.MaterialIndex >= 0 && triangle.MaterialIndex < mesh.Materials.Count &&
                               string.Equals(mesh.Materials[triangle.MaterialIndex].TextureName, materialTag,
                                   StringComparison.OrdinalIgnoreCase);
                HashSet<int> destination = matches ? selected : other;
                destination.Add(triangle.First);
                destination.Add(triangle.Second);
                destination.Add(triangle.Third);
            }
            if (selected.Count == 0) continue;
            if (mesh.GeometrySource == null)
                throw new InvalidDataException(
                    $"The '{materialTag}' surface is not stored in an editable embedded clump.");
            if (selected.Overlaps(other))
                throw new InvalidDataException(
                    $"The '{materialTag}' surface shares vertices with another material and cannot be moved safely.");
            if (!usedGeometryOffsets.Add(mesh.GeometrySource.PositionDataOffset))
                throw new InvalidDataException("The home-run clump reuses one geometry in multiple atomics.");
            if (!Matrix4x4.Invert(mesh.GeometrySource.LocalToWorld, out Matrix4x4 worldToLocal))
                throw new InvalidDataException("The home-run clump has a non-invertible frame transform.");
            if (mesh.GeometrySource.PositionDataOffset < 0 ||
                mesh.GeometrySource.PositionDataOffset + mesh.Vertices.Count * 12 > rawData.Length ||
                mesh.GeometrySource.BoundingSphereOffset < 0 ||
                mesh.GeometrySource.BoundingSphereOffset + 16 > rawData.Length)
                throw new InvalidDataException("The home-run clump points outside the RWS payload.");
            targets.Add(new TargetMesh(meshIndex, mesh, selected, mesh.GeometrySource, worldToLocal));
        }

        if (targets.Count == 0)
            throw new InvalidDataException(
                $"No isolated embedded-clump geometry uses material '{materialTag}'.");
        return new StadiumHomeRunBoundaryDocument(scene, rawData, materialTag, targets);
    }

    public void Apply(Vector3 offset, Vector3 scale)
    {
        if (!IsFinite(offset) || !IsFinite(scale) || scale.X <= 0 || scale.Y <= 0 || scale.Z <= 0)
            throw new ArgumentOutOfRangeException(nameof(scale), "Offsets must be finite and scale must be positive.");

        Offset = offset;
        Scale = scale;
        _currentData = _originalData.ToArray();
        Vector3 pivot = OriginalBoundary.Center;
        List<RenderWareSceneMesh> previewMeshes = _originalScene.Meshes.ToList();

        foreach (TargetMesh target in _targets)
        {
            RenderWareSceneVertex[] vertices = target.Mesh.Vertices.ToArray();
            for (int index = 0; index < vertices.Length; index++)
            {
                Vector3 world = vertices[index].Position;
                if (target.VertexIndices.Contains(index))
                    world = pivot + Vector3.Multiply(world - pivot, scale) + offset;
                Vector3 local = Vector3.Transform(world, target.WorldToLocal);
                WriteVector(_currentData, target.Source.PositionDataOffset + index * 12, local);
                vertices[index] = vertices[index] with { Position = world };
            }
            UpdateBoundingSphere(_currentData, target.Source.BoundingSphereOffset, vertices,
                target.WorldToLocal);
            previewMeshes[target.MeshIndex] = target.Mesh with { Vertices = vertices };
        }

        PreviewScene = CloneScene(_originalScene, previewMeshes);
    }

    public void Reset()
    {
        Offset = Vector3.Zero;
        Scale = Vector3.One;
        _currentData = _originalData.ToArray();
        PreviewScene = _originalScene;
    }

    public byte[] Serialize() => _currentData.ToArray();

    private static void UpdateBoundingSphere(
        byte[] data,
        int offset,
        IReadOnlyList<RenderWareSceneVertex> vertices,
        Matrix4x4 worldToLocal)
    {
        Vector3 minimum = new(float.MaxValue), maximum = new(float.MinValue);
        Vector3[] local = new Vector3[vertices.Count];
        for (int index = 0; index < vertices.Count; index++)
        {
            local[index] = Vector3.Transform(vertices[index].Position, worldToLocal);
            minimum = Vector3.Min(minimum, local[index]);
            maximum = Vector3.Max(maximum, local[index]);
        }
        Vector3 center = (minimum + maximum) * 0.5F;
        float radius = local.Max(position => Vector3.Distance(center, position));
        WriteVector(data, offset, center);
        WriteFloat(data, offset + 12, radius);
    }

    private static RenderWareScene CloneScene(
        RenderWareScene source,
        IReadOnlyList<RenderWareSceneMesh> meshes)
    {
        RenderWareScene result = new(source.SourcePath, source.Kind, meshes, source.Chunks,
            source.PlaneSectorCount, source.WorldSectorCount, source.EmbeddedClumpCount,
            source.NativeTextureNames, source.Warnings.ToList());
        foreach ((string name, RenderWareTexture texture) in source.Textures)
            result.AddTexture(name, texture);
        return result;
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static void WriteVector(byte[] data, int offset, Vector3 value)
    {
        WriteFloat(data, offset, value.X);
        WriteFloat(data, offset + 4, value.Y);
        WriteFloat(data, offset + 8, value.Z);
    }

    private static void WriteFloat(byte[] data, int offset, float value) =>
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset, 4), BitConverter.SingleToInt32Bits(value));

    private sealed record TargetMesh(
        int MeshIndex,
        RenderWareSceneMesh Mesh,
        HashSet<int> VertexIndices,
        RenderWareGeometrySource Source,
        Matrix4x4 WorldToLocal);
}
