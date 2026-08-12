using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;

namespace PS2_DATA_File_Extractor.FileOperations;

public sealed record StadiumAmbientAnimationAssignment(
    string Directive,
    string AssetName)
{
    public bool PlaysOnce => Directive.Contains("Once", StringComparison.OrdinalIgnoreCase);
    public bool IsHomeRun => Directive.StartsWith("hr", StringComparison.OrdinalIgnoreCase);
    public override string ToString() => $"{AssetName} ({Directive})";
}

public sealed record StadiumAmbientVisual(
    int AmbientIndex,
    string Name,
    bool IsLoaded,
    string AssetKind,
    string? AssetPath,
    Vector3? Anchor,
    IReadOnlyList<Vector3> PathPoints,
    IReadOnlyList<StadiumAmbientAnimationAssignment> Animations,
    bool ModelVisible,
    string Note,
    Matrix4x4? ModelTransform);

public sealed record StadiumAmbientPreviewResult(
    RenderWareScene Scene,
    IReadOnlyList<StadiumAmbientVisual> Items,
    int VisibleModelCount,
    int ResolvedModelCount,
    int PathCount);

public sealed record StadiumAmbientPathSample(Vector3 Position, Vector3 Direction);

public static class StadiumAmbientPreviewBuilder
{
    public static StadiumAmbientPreviewResult Build(
        RenderWareSceneArchive archive,
        StadiumEnvironment stadium,
        RenderWareScene fieldScene,
        IDictionary<string, RenderWareScene> modelCache,
        int selectedAmbient,
        bool showModels,
        bool showDisabled,
        IReadOnlyDictionary<string, StadiumSplineDocument>? splineDocuments = null)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(stadium);
        ArgumentNullException.ThrowIfNull(fieldScene);
        ArgumentNullException.ThrowIfNull(modelCache);

        List<RenderWareSceneMesh> meshes = new(fieldScene.Meshes);
        List<StadiumAmbientVisual> visuals = new(stadium.Document.Ambients.Count);
        List<(string Alias, RenderWareTexture Texture)> textures = fieldScene.Textures
            .Select(pair => (pair.Key, pair.Value)).ToList();
        int declared = stadium.Document.DeclaredAmbientCount;
        int visibleModels = 0, resolvedModels = 0, paths = 0;

        foreach (FieldDataAmbient ambient in stadium.Document.Ambients)
        {
            bool loaded = ambient.Index < declared;
            string pathValue = Setting(ambient, "path") ?? $"Fields/{stadium.FolderName}";
            string? modelValue = Setting(ambient, "model");
            string assetKind = "Model";
            if (string.IsNullOrWhiteSpace(modelValue))
            {
                modelValue = Setting(ambient, "particle");
                assetKind = string.IsNullOrWhiteSpace(modelValue) ? string.Empty : "Particle source";
            }
            RenderWareAssetFile? asset = string.IsNullOrWhiteSpace(modelValue)
                ? null
                : archive.FindAmbientModel(pathValue, modelValue, stadium.FolderName);
            if (asset != null) resolvedModels++;

            IReadOnlyList<Vector3> pathPoints = ReadSpline(
                archive, Setting(ambient, "spline"), splineDocuments);
            if (pathPoints.Count > 1) paths++;
            Vector3? position = ReadPosition(ambient, pathPoints);
            Vector3 hpr = ReadHpr(ambient);
            IReadOnlyList<StadiumAmbientAnimationAssignment> animations = ambient.Settings
                .Where(setting => setting.Key.Contains("anim", StringComparison.OrdinalIgnoreCase))
                .Select(setting => new StadiumAmbientAnimationAssignment(
                    setting.Key, setting.Value.Split(';', 2)[0].Trim()))
                .Where(value => value.AssetName.Length > 0)
                .DistinctBy(value => $"{value.Directive}\0{value.AssetName}", StringComparer.OrdinalIgnoreCase)
                .ToList();

            bool modelVisible = showModels && asset != null && position.HasValue && (loaded || showDisabled);
            string note;
            if (asset == null && !string.IsNullOrWhiteSpace(modelValue)) note = $"Could not resolve {modelValue}.";
            else if (asset == null && Setting(ambient, "movie") is string movie) note = $"Runtime movie: {movie}.";
            else if (asset == null) note = "No DFF model in this block.";
            else if (!position.HasValue) note = "Model resolved; no fixed position or readable spline point.";
            else if (!loaded) note = showDisabled ? "Disabled by numAmbs; shown translucent." : "Disabled by numAmbs.";
            else note = pathPoints.Count > 1 ? $"Placed at spline start; {pathPoints.Count} path points." : "Placed from fielddata coordinates.";

            if (modelVisible)
            {
                try
                {
                    RenderWareScene model = GetModel(archive, asset!, modelCache);
                    AddModel(meshes, textures, model, ambient.Index, ambient.DisplayName,
                        CreateTransform(position!.Value, hpr), loaded, ambient.Index == selectedAmbient);
                    visibleModels++;
                }
                catch (Exception exception) when (exception is InvalidDataException or ArgumentException or IOException)
                {
                    modelVisible = false;
                    note = "Model preview failed: " + exception.Message;
                }
            }

            Matrix4x4? modelTransform = position.HasValue ? CreateTransform(position.Value, hpr) : null;
            visuals.Add(new StadiumAmbientVisual(ambient.Index, ambient.DisplayName, loaded,
                assetKind, asset?.Path, position, pathPoints, animations, modelVisible, note, modelTransform));
        }

        RenderWareScene combined = new(fieldScene.SourcePath, fieldScene.Kind, meshes, fieldScene.Chunks,
            fieldScene.PlaneSectorCount, fieldScene.WorldSectorCount, fieldScene.EmbeddedClumpCount,
            fieldScene.NativeTextureNames, fieldScene.Warnings.ToList());
        foreach ((string alias, RenderWareTexture texture) in textures) combined.AddTexture(alias, texture);
        return new StadiumAmbientPreviewResult(combined, visuals, visibleModels, resolvedModels, paths);
    }

    public static IReadOnlyList<Vector3> ParseSpline(ReadOnlySpan<byte> data)
    {
        const int countOffset = 0x2c, pointsOffset = 0x34, pointSize = 12;
        if (data.Length < pointsOffset || BinaryPrimitives.ReadUInt32LittleEndian(data) != 0x0c) return [];
        int count = BinaryPrimitives.ReadInt32LittleEndian(data[countOffset..]);
        if (count <= 0 || count > 100_000 || pointsOffset + (long)count * pointSize > data.Length) return [];
        List<Vector3> points = new(count);
        for (int index = 0; index < count; index++)
        {
            int offset = pointsOffset + index * pointSize;
            float x = ReadFloat(data[offset..]);
            float y = ReadFloat(data[(offset + 4)..]);
            float z = ReadFloat(data[(offset + 8)..]);
            if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z)) return [];
            points.Add(new Vector3(x, y, z));
        }
        return points;
    }

    public static StadiumAmbientPathSample SamplePath(IReadOnlyList<Vector3> points, float progress)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count == 0) return new StadiumAmbientPathSample(Vector3.Zero, Vector3.UnitZ);
        if (points.Count == 1) return new StadiumAmbientPathSample(points[0], Vector3.UnitZ);
        progress = Math.Clamp(progress, 0F, 1F);
        float total = 0F;
        float[] lengths = new float[points.Count - 1];
        for (int index = 0; index < lengths.Length; index++)
        {
            lengths[index] = Vector3.Distance(points[index], points[index + 1]);
            total += lengths[index];
        }
        if (total <= 0.000001F) return new StadiumAmbientPathSample(points[0], Vector3.UnitZ);
        float wanted = total * progress, passed = 0F;
        for (int index = 0; index < lengths.Length; index++)
        {
            float length = lengths[index];
            if (wanted > passed + length && index < lengths.Length - 1)
            {
                passed += length;
                continue;
            }
            Vector3 direction = length <= 0.000001F ? Vector3.UnitZ :
                Vector3.Normalize(points[index + 1] - points[index]);
            float amount = length <= 0.000001F ? 0F : Math.Clamp((wanted - passed) / length, 0F, 1F);
            return new StadiumAmbientPathSample(Vector3.Lerp(points[index], points[index + 1], amount), direction);
        }
        Vector3 lastDirection = points[^1] - points[^2];
        if (lastDirection.LengthSquared() <= 0.000001F) lastDirection = Vector3.UnitZ;
        else lastDirection = Vector3.Normalize(lastDirection);
        return new StadiumAmbientPathSample(points[^1], lastDirection);
    }

    public static float GetPreviewSpeed(FieldDataAmbient ambient)
    {
        ArgumentNullException.ThrowIfNull(ambient);
        float[]? speed = Numbers(Setting(ambient, "speed"), 1);
        if (speed is { Length: > 0 } && speed[0] > 0F) return speed[0];
        float[]? random = Numbers(Setting(ambient, "randFloatSpeed"), 2);
        if (random is { Length: > 1 })
        {
            float average = (Math.Abs(random[0]) + Math.Abs(random[1])) * 0.5F;
            if (average > 0F) return average;
        }
        return 1F;
    }

    public static double EstimatePreviewDuration(FieldDataAmbient ambient) =>
        Math.Clamp(12D / GetPreviewSpeed(ambient), 2D, 60D);

    public static float GetAnimationPlaybackTime(
        double playbackPosition,
        double pathDuration,
        float animationDuration,
        bool syncToPath,
        bool loopAnimation)
    {
        if (!float.IsFinite(animationDuration) || animationDuration <= 0) return 0;
        double time = syncToPath && pathDuration > 0
            ? Math.Clamp(playbackPosition / pathDuration, 0D, 1D) * animationDuration
            : Math.Max(0D, playbackPosition);
        if (loopAnimation)
        {
            time %= animationDuration;
            if (time < 0) time += animationDuration;
        }
        return (float)Math.Clamp(time, 0D, animationDuration);
    }

    public static Matrix4x4 CreatePlaybackDelta(FieldDataAmbient ambient, Vector3 basePosition,
        StadiumAmbientPathSample sample, bool facePath)
    {
        ArgumentNullException.ThrowIfNull(ambient);
        Vector3 baseHpr = ReadHpr(ambient);
        Vector3 currentHpr = baseHpr;
        if (facePath && sample.Direction.LengthSquared() > 0.000001F)
            currentHpr.X += MathF.Atan2(sample.Direction.X, sample.Direction.Z) * 180F / MathF.PI;
        Matrix4x4 baseline = CreateTransform(basePosition, baseHpr);
        return Matrix4x4.Invert(baseline, out Matrix4x4 inverse)
            ? inverse * CreateTransform(sample.Position, currentHpr)
            : Matrix4x4.CreateTranslation(sample.Position - basePosition);
    }

    private static RenderWareScene GetModel(RenderWareSceneArchive archive, RenderWareAssetFile asset,
        IDictionary<string, RenderWareScene> cache)
    {
        if (!cache.TryGetValue(asset.Path, out RenderWareScene? model))
        {
            model = archive.LoadScene(asset);
            cache[asset.Path] = model;
        }
        return model;
    }

    private static void AddModel(List<RenderWareSceneMesh> destination,
        List<(string Alias, RenderWareTexture Texture)> textures, RenderWareScene model,
        int ambientIndex, string displayName, Matrix4x4 transform, bool loaded, bool selected)
    {
        string prefix = $"ambient_{ambientIndex:00}";
        foreach ((string name, RenderWareTexture texture) in model.Textures)
            textures.Add(($"{prefix}_{name}", texture));
        foreach (RenderWareSceneMesh mesh in model.Meshes)
        {
            List<RenderWareSceneVertex> vertices = mesh.Vertices.Select(vertex =>
            {
                Vector3 normal = Vector3.TransformNormal(vertex.Normal, transform);
                if (normal.LengthSquared() > 0.000001F) normal = Vector3.Normalize(normal);
                return vertex with
                {
                    Position = Vector3.Transform(vertex.Position, transform),
                    Normal = normal
                };
            }).ToList();
            List<RenderWareMaterial> materials = mesh.Materials.Select(material =>
            {
                string? textureName = string.IsNullOrWhiteSpace(material.TextureName)
                    ? null : $"{prefix}_{material.TextureName}";
                Color color = PreviewColor(material.Color, loaded, selected);
                return material with { TextureName = textureName, Color = color };
            }).ToList();
            destination.Add(new RenderWareSceneMesh($"Ambient {ambientIndex + 1:00}: {displayName} / {mesh.Name}",
                vertices, mesh.Triangles, materials, loaded ? "Ambient model" : "Disabled ambient model"));
        }
    }

    private static Color PreviewColor(Color color, bool loaded, bool selected)
    {
        int alpha = loaded ? color.A : Math.Min((int)color.A, 105);
        if (!selected) return Color.FromArgb(alpha, color.R, color.G, color.B);
        return Color.FromArgb(Math.Max(alpha, 180), Math.Min(255, color.R + 70),
            Math.Min(255, color.G + 55), Math.Max(0, (int)(color.B * 0.55F)));
    }

    private static Matrix4x4 CreateTransform(Vector3 position, Vector3 hpr)
    {
        const float degrees = MathF.PI / 180F;
        return Matrix4x4.CreateRotationZ(hpr.Z * degrees) *
               Matrix4x4.CreateRotationX(hpr.Y * degrees) *
               Matrix4x4.CreateRotationY(hpr.X * degrees) *
               Matrix4x4.CreateTranslation(position);
    }

    private static Vector3? ReadPosition(FieldDataAmbient ambient, IReadOnlyList<Vector3> path)
    {
        float[]? position = Numbers(Setting(ambient, "pos"), 3);
        if (position != null) return new Vector3(position[0], position[1], position[2]);
        float[]? relative = Numbers(Setting(ambient, "relPosHpr"), 6);
        if (relative != null) return new Vector3(relative[0], relative[1], relative[2]);
        return path.Count > 0 ? path[0] : null;
    }

    private static Vector3 ReadHpr(FieldDataAmbient ambient)
    {
        float[]? hpr = Numbers(Setting(ambient, "hpr"), 3);
        if (hpr != null) return new Vector3(hpr[0], hpr[1], hpr[2]);
        float[]? relative = Numbers(Setting(ambient, "relPosHpr"), 6);
        return relative == null ? Vector3.Zero : new Vector3(relative[3], relative[4], relative[5]);
    }

    private static IReadOnlyList<Vector3> ReadSpline(RenderWareSceneArchive archive, string? value,
        IReadOnlyDictionary<string, StadiumSplineDocument>? splineDocuments)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];
        string path = StadiumSplineDocument.NormalizePath(value);
        if (splineDocuments?.TryGetValue(path, out StadiumSplineDocument? document) == true)
            return document.Points;
        byte[]? data = archive.ReadRawPath(path);
        return data == null ? [] : ParseSpline(data);
    }

    private static string? Setting(FieldDataAmbient ambient, string key) => ambient.Settings
        .FirstOrDefault(setting => setting.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Value;

    private static float[]? Numbers(string? value, int minimum)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string[] parts = value.Replace(';', ' ').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < minimum) return null;
        float[] result = new float[parts.Length];
        for (int index = 0; index < parts.Length; index++)
            if (!float.TryParse(parts[index], NumberStyles.Float, CultureInfo.InvariantCulture, out result[index]) ||
                !float.IsFinite(result[index])) return null;
        return result;
    }

    private static float ReadFloat(ReadOnlySpan<byte> data) =>
        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data));
}
