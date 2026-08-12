using System.Numerics;

namespace PS2_DATA_File_Extractor.FileOperations;

public enum StadiumHomeRunEventKind
{
    Animation,
    Particle,
    Sound,
    Mixed
}

public sealed record StadiumHomeRunEvent(
    FieldDataAmbient Ambient,
    StadiumHomeRunEventKind Kind,
    double DelaySeconds,
    string? Sound,
    IReadOnlyList<FieldDataSetting> Settings)
{
    public string DisplayName => $"{Ambient.Index + 1:00}. {Ambient.DisplayName}";
}

public sealed record StadiumHomeRunBoundary(
    string MaterialTag,
    int TriangleCount,
    int MeshCount,
    Vector3 Minimum,
    Vector3 Maximum)
{
    public bool IsPresent => TriangleCount > 0;
    public Vector3 Center => (Minimum + Maximum) * 0.5F;
    public Vector3 Size => Maximum - Minimum;
}

public static class StadiumHomeRunAnalyzer
{
    private static readonly HashSet<string> EventKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "hrAnim", "hrAnimOnce", "hrAnimOnly", "hrParticleOnceOnly", "hrDelay", "hrSfx",
        "anim", "animOnce", "particleActive", "startColor", "endColor", "model", "particle",
        "path", "spline", "pos", "hpr", "relPosHpr", "speed", "randFloatSpeed", "collision"
    };

    public static IReadOnlyList<StadiumHomeRunEvent> FindEvents(FieldDataDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        List<StadiumHomeRunEvent> result = [];
        foreach (FieldDataAmbient ambient in document.Ambients)
        {
            bool animation = ambient.Settings.Any(setting =>
                setting.Key.StartsWith("hrAnim", StringComparison.OrdinalIgnoreCase));
            bool particle = ambient.Settings.Any(setting =>
                setting.Key.StartsWith("hrParticle", StringComparison.OrdinalIgnoreCase));
            bool sound = ambient.Settings.Any(setting =>
                setting.Key.Equals("hrSfx", StringComparison.OrdinalIgnoreCase));
            if (!animation && !particle && !sound) continue;
            StadiumHomeRunEventKind kind = (animation ? 1 : 0) + (particle ? 1 : 0) + (sound ? 1 : 0) > 1
                ? StadiumHomeRunEventKind.Mixed
                : animation ? StadiumHomeRunEventKind.Animation
                : particle ? StadiumHomeRunEventKind.Particle
                : StadiumHomeRunEventKind.Sound;
            FieldDataSetting? delay = ambient.Settings.FirstOrDefault(setting =>
                setting.Key.Equals("hrDelay", StringComparison.OrdinalIgnoreCase));
            double.TryParse(delay?.Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double seconds);
            string? sfx = ambient.Settings.FirstOrDefault(setting =>
                setting.Key.Equals("hrSfx", StringComparison.OrdinalIgnoreCase))?.Value;
            result.Add(new StadiumHomeRunEvent(ambient, kind, Math.Max(0, seconds), sfx,
                ambient.Settings.Where(setting => EventKeys.Contains(setting.Key) ||
                    setting.Key.StartsWith("hr", StringComparison.OrdinalIgnoreCase)).ToList()));
        }
        return result;
    }

    public static string HomeRunMaterialTag(FieldDataDocument document) =>
        document.CollisionSettings.FirstOrDefault(setting =>
            setting.Key.Equals("homerun", StringComparison.OrdinalIgnoreCase))?.Value.Trim()
        ?? "HR";

    public static StadiumHomeRunBoundary AnalyzeBoundary(RenderWareScene scene, string materialTag)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentException.ThrowIfNullOrWhiteSpace(materialTag);
        int triangles = 0, meshes = 0;
        Vector3 minimum = new(float.MaxValue), maximum = new(float.MinValue);
        foreach (RenderWareSceneMesh mesh in scene.Meshes)
        {
            bool used = false;
            foreach (RenderWareTriangle triangle in mesh.Triangles)
            {
                if (triangle.MaterialIndex < 0 || triangle.MaterialIndex >= mesh.Materials.Count ||
                    !string.Equals(mesh.Materials[triangle.MaterialIndex].TextureName, materialTag,
                        StringComparison.OrdinalIgnoreCase)) continue;
                foreach (int index in new[] { triangle.First, triangle.Second, triangle.Third })
                {
                    if (index < 0 || index >= mesh.Vertices.Count) continue;
                    Vector3 position = mesh.Vertices[index].Position;
                    minimum = Vector3.Min(minimum, position);
                    maximum = Vector3.Max(maximum, position);
                }
                triangles++;
                used = true;
            }
            if (used) meshes++;
        }
        if (triangles == 0) minimum = maximum = Vector3.Zero;
        return new StadiumHomeRunBoundary(materialTag, triangles, meshes, minimum, maximum);
    }
}
