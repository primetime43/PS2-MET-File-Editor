using System.Numerics;

namespace PS2_DATA_File_Extractor.FileOperations;

/// <summary>
/// Field-relative coordinates recovered from the retail SLUS_208.65 executable.
/// Y is up, home plate is the origin, and center field extends toward negative Z.
/// </summary>
public static class BackyardFieldCoordinates
{
    public static readonly IReadOnlyDictionary<string, Vector3> Bases =
        new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase)
        {
            ["Home plate"] = new(0F, 0F, 0F),
            ["First base"] = new(814.5F, 0F, -848F),
            ["Second base"] = new(0F, 0F, -1696F),
            ["Third base"] = new(-814.5F, 0F, -848F)
        };

    // BaseballCamera.cpp routines in the retail executable. PitcherCam does not
    // set HPR itself; the preview aims that recovered position toward home plate.
    public static readonly IReadOnlyList<BackyardCameraPreset> CameraPresets =
    [
        new("Game batting camera", new(0F, 75.2F, 509.1F), 180F, 1.1F,
            "SetBattingView @ 0x0013C160"),
        LookAt("Game pitcher camera", new(485.5F, 85.7F, -189F), new(0F, 70F, 0F),
            "PitcherCam @ 0x0013B740; look-at inferred from gameplay target"),
        new("Game infield camera", new(-1007.85F, 3274.28F, 1817.1F), -160.3F, -47.8F,
            "SetInfieldPositionCam @ 0x0013B470"),
        new("Game fielder camera", new(0F, 1480F, -32F), -180F, -30F,
            "SetFielderPositionCam @ 0x0013B560"),
        LookAt("Home plate POV", new(0F, 105F, 45F), new(0F, 65F, -1696F),
            "Home spawn @ (0,0,0); preview eye-height offset"),
        LookAt("Pitcher's mound POV", new(0F, 105F, -1128F), new(0F, 65F, 0F),
            "Regulation field-relative mound estimate; faces home plate"),
        LookAt("First base POV", new(814.5F, 105F, -848F), new(0F, 65F, 0F),
            "First-base spawn from retail fieldPositions table"),
        LookAt("Second base POV", new(0F, 105F, -1696F), new(0F, 65F, 0F),
            "Second-base spawn from retail fieldPositions table"),
        LookAt("Third base POV", new(-814.5F, 105F, -848F), new(0F, 65F, 0F),
            "Third-base spawn from retail fieldPositions table")
    ];

    // sInfieldPositions @ 0x00663C30, populated by __sinit_BaseballPlayer.cpp
    // at 0x005E6250. Each entry is an X/Z pair; Y is terrain-derived at runtime.
    public static readonly IReadOnlyList<Vector2> InfieldSpawns =
    [
        new(900, -1050), new(500, -1700), new(-900, -1050), new(-500, -1700),
        new(900, -950), new(550, -1650), new(-900, -950), new(-550, -1650),
        new(700, -800), new(510, -1600), new(-700, -800), new(-510, -1600),
        new(1000, -1150), new(530, -1750), new(-1000, -1150), new(-530, -1750),
        new(700, -900), new(420, -1440), new(-700, -900), new(-420, -1440),
        new(720, -920), new(400, -1600), new(-850, -1070), new(-500, -1600)
    ];

    // sOutfieldPositions @ 0x00663D50. Three successive nine-position layouts.
    public static readonly IReadOnlyList<Vector2> OutfieldSpawns =
    [
        new(-1650, -2100), new(-350, -3100), new(1100, -2400),
        new(-1400, -2200), new(0, -3200), new(1400, -2200),
        new(-1100, -2400), new(350, -3100), new(1650, -2100),
        new(-1800, -2300), new(-400, -3400), new(1200, -2600),
        new(-1500, -2500), new(0, -3500), new(1500, -2500),
        new(-1200, -2600), new(400, -3400), new(1800, -2300),
        new(-1900, -2600), new(-450, -3600), new(1300, -2800),
        new(-1600, -2700), new(0, -3800), new(1600, -2700),
        new(-1300, -2800), new(450, -3600), new(1900, -2600)
    ];

    private static BackyardCameraPreset LookAt(string name, Vector3 position, Vector3 target, string source)
    {
        Vector3 direction = Vector3.Normalize(target - position);
        float heading = MathF.Atan2(direction.X, direction.Z) * 180F / MathF.PI;
        float pitch = -MathF.Asin(direction.Y) * 180F / MathF.PI;
        return new BackyardCameraPreset(name, position, heading, pitch, source);
    }
}

public sealed record BackyardCameraPreset(string Name, Vector3 Position,
    float HeadingDegrees, float PitchDegrees, string Source)
{
    public override string ToString() => Name;
}
