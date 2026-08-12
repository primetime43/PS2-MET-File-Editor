using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;

namespace PS2_DATA_File_Extractor.FileOperations;

public sealed record StadiumAmbientVisual(
    int AmbientIndex,
    string Name,
    bool IsLoaded,
    string AssetKind,
    string? AssetPath,
    Vector3? Anchor,
    IReadOnlyList<Vector3> PathPoints,
    IReadOnlyList<string> Animations,
    bool ModelVisible,
    string Note);

public sealed record StadiumAmbientPreviewResult(
    RenderWareScene Scene,
    IReadOnlyList<StadiumAmbientVisual> Items,
    int VisibleModelCount,
    int ResolvedModelCount,
    int PathCount);

public static class StadiumAmbientPreviewBuilder
{
    public static StadiumAmbientPreviewResult Build(
        RenderWareSceneArchive archive,
        StadiumEnvironment stadium,
        RenderWareScene fieldScene,
        IDictionary<string, RenderWareScene> modelCache,
        int selectedAmbient,
        bool showModels,
        bool showDisabled)
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

            IReadOnlyList<Vector3> pathPoints = ReadSpline(archive, Setting(ambient, "spline"));
            if (pathPoints.Count > 1) paths++;
            Vector3? position = ReadPosition(ambient, pathPoints);
            Vector3 hpr = ReadHpr(ambient);
            IReadOnlyList<string> animations = ambient.Settings
                .Where(setting => setting.Key.Contains("anim", StringComparison.OrdinalIgnoreCase))
                .Select(setting => setting.Value.Split(';', 2)[0].Trim())
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
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

            visuals.Add(new StadiumAmbientVisual(ambient.Index, ambient.DisplayName, loaded,
                assetKind, asset?.Path, position, pathPoints, animations, modelVisible, note));
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
        return hpr == null ? Vector3.Zero : new Vector3(hpr[0], hpr[1], hpr[2]);
    }

    private static IReadOnlyList<Vector3> ReadSpline(RenderWareSceneArchive archive, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];
        string path = value.Split(';', 2)[0].Trim().TrimStart('/').Replace('\\', '/');
        byte[]? data = archive.ReadRawPath(path.StartsWith("data/", StringComparison.OrdinalIgnoreCase)
            ? path : "data/" + path);
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
