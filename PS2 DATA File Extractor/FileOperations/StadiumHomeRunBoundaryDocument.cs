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
    private readonly List<EditablePoint> _points;
    private readonly List<StadiumHomeRunBoundaryTriangle> _triangles;
    private readonly Dictionary<VertexKey, int> _pointByVertex;
    private readonly Dictionary<int, Vector3> _pointDeltas = [];
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
        (_points, _triangles, _pointByVertex) = BuildTopology(targets, materialTag);
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
    public int ModifiedPointCount => _pointDeltas.Count;
    public bool IsChanged => Offset != Vector3.Zero || Scale != Vector3.One || _pointDeltas.Count > 0;
    public IReadOnlyList<StadiumHomeRunBoundaryTriangle> Triangles => _triangles;
    public IReadOnlyList<StadiumHomeRunBoundaryVertex> Vertices => _points
        .Select((point, index) => new StadiumHomeRunBoundaryVertex(index, point.OriginalPosition,
            CurrentPosition(index), point.References.Count, _pointDeltas.ContainsKey(index)))
        .ToList();

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
        Rebuild();
    }

    public void SetVertexPosition(int pointIndex, Vector3 position)
    {
        SetVertexPositions(new Dictionary<int, Vector3> { [pointIndex] = position });
    }

    public void SetVertexPositions(IReadOnlyDictionary<int, Vector3> positions)
    {
        ArgumentNullException.ThrowIfNull(positions);
        foreach ((int pointIndex, Vector3 position) in positions)
        {
            if ((uint)pointIndex >= (uint)_points.Count) throw new ArgumentOutOfRangeException(nameof(positions));
            if (!IsFinite(position)) throw new ArgumentOutOfRangeException(nameof(positions), "Positions must be finite.");
        }
        foreach ((int pointIndex, Vector3 position) in positions)
        {
            Vector3 baseline = TransformedOriginal(_points[pointIndex].OriginalPosition);
            Vector3 delta = position - baseline;
            if (delta.LengthSquared() <= 0.000001F) _pointDeltas.Remove(pointIndex);
            else _pointDeltas[pointIndex] = delta;
        }
        if (positions.Count > 0) Rebuild();
    }

    public void ResetVertex(int pointIndex)
    {
        ResetVertices([pointIndex]);
    }

    public void ResetVertices(IEnumerable<int> pointIndices)
    {
        ArgumentNullException.ThrowIfNull(pointIndices);
        int[] indices = pointIndices.Distinct().ToArray();
        if (indices.Any(index => (uint)index >= (uint)_points.Count))
            throw new ArgumentOutOfRangeException(nameof(pointIndices));
        bool changed = false;
        foreach (int index in indices) changed |= _pointDeltas.Remove(index);
        if (changed) Rebuild();
    }

    private void Rebuild()
    {
        if (Offset == Vector3.Zero && Scale == Vector3.One && _pointDeltas.Count == 0)
        {
            _currentData = _originalData.ToArray();
            PreviewScene = _originalScene;
            return;
        }
        _currentData = _originalData.ToArray();
        List<RenderWareSceneMesh> previewMeshes = _originalScene.Meshes.ToList();

        foreach (TargetMesh target in _targets)
        {
            RenderWareSceneVertex[] vertices = target.Mesh.Vertices.ToArray();
            for (int index = 0; index < vertices.Length; index++)
            {
                Vector3 world = vertices[index].Position;
                if (target.VertexIndices.Contains(index))
                {
                    world = TransformedOriginal(world);
                    if (_pointByVertex.TryGetValue(new VertexKey(target.MeshIndex, index), out int pointIndex) &&
                        _pointDeltas.TryGetValue(pointIndex, out Vector3 delta))
                        world += delta;
                }
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
        _pointDeltas.Clear();
        _currentData = _originalData.ToArray();
        PreviewScene = _originalScene;
    }

    public byte[] Serialize() => _currentData.ToArray();

    private Vector3 CurrentPosition(int pointIndex) =>
        TransformedOriginal(_points[pointIndex].OriginalPosition) +
        (_pointDeltas.TryGetValue(pointIndex, out Vector3 delta) ? delta : Vector3.Zero);

    private Vector3 TransformedOriginal(Vector3 position) =>
        OriginalBoundary.Center + Vector3.Multiply(position - OriginalBoundary.Center, Scale) + Offset;

    private static (List<EditablePoint> Points, List<StadiumHomeRunBoundaryTriangle> Triangles,
        Dictionary<VertexKey, int> PointByVertex) BuildTopology(
        IReadOnlyList<TargetMesh> targets,
        string materialTag)
    {
        List<EditablePoint> points = [];
        Dictionary<VertexKey, int> pointByVertex = [];
        foreach (TargetMesh target in targets)
        foreach (int vertexIndex in target.VertexIndices.Order())
        {
            Vector3 position = target.Mesh.Vertices[vertexIndex].Position;
            int pointIndex = points.FindIndex(point =>
                Vector3.DistanceSquared(point.OriginalPosition, position) <= 0.000001F);
            if (pointIndex < 0)
            {
                pointIndex = points.Count;
                points.Add(new EditablePoint(position, []));
            }
            VertexKey reference = new(target.MeshIndex, vertexIndex);
            points[pointIndex].References.Add(reference);
            pointByVertex[reference] = pointIndex;
        }

        List<StadiumHomeRunBoundaryTriangle> triangles = [];
        HashSet<(int, int, int)> seen = [];
        foreach (TargetMesh target in targets)
        foreach (RenderWareTriangle triangle in target.Mesh.Triangles)
        {
            if (triangle.MaterialIndex < 0 || triangle.MaterialIndex >= target.Mesh.Materials.Count ||
                !string.Equals(target.Mesh.Materials[triangle.MaterialIndex].TextureName, materialTag,
                    StringComparison.OrdinalIgnoreCase)) continue;
            if (!pointByVertex.TryGetValue(new VertexKey(target.MeshIndex, triangle.First), out int first) ||
                !pointByVertex.TryGetValue(new VertexKey(target.MeshIndex, triangle.Second), out int second) ||
                !pointByVertex.TryGetValue(new VertexKey(target.MeshIndex, triangle.Third), out int third)) continue;
            int[] ordered = [first, second, third];
            Array.Sort(ordered);
            if (seen.Add((ordered[0], ordered[1], ordered[2])))
                triangles.Add(new StadiumHomeRunBoundaryTriangle(first, second, third));
        }
        return (points, triangles, pointByVertex);
    }

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

    private sealed record EditablePoint(Vector3 OriginalPosition, List<VertexKey> References);
    private readonly record struct VertexKey(int MeshIndex, int VertexIndex);
}

public sealed record StadiumHomeRunBoundaryVertex(
    int Index,
    Vector3 OriginalPosition,
    Vector3 Position,
    int RawVertexCount,
    bool IsModified);

public sealed record StadiumHomeRunBoundaryTriangle(int First, int Second, int Third);
