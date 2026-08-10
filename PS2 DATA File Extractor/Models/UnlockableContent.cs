namespace PS2_DATA_File_Extractor.Models;

public sealed record UnlockableContent(string Name, uint Mask, string Category)
{
    public static IReadOnlyList<UnlockableContent> Items { get; } = new[]
    {
        new UnlockableContent("Abner Dubbleplay", 1u << 0, "Player"),
        new UnlockableContent("Mr. Clanky", 1u << 1, "Player"),
        new UnlockableContent("Barry DeJay", 1u << 2, "Player"),
        new UnlockableContent("Randy Johnson", 1u << 3, "Player"),
        new UnlockableContent("Pedro Martinez", 1u << 4, "Player"),
        new UnlockableContent("Mike Piazza", 1u << 5, "Player"),
        new UnlockableContent("Derek Jeter", 1u << 6, "Player"),
        new UnlockableContent("Greg Maddux", 1u << 7, "Player"),
        new UnlockableContent("Shawn Green", 1u << 8, "Player"),
        new UnlockableContent("Humongous Entertainment Stadium", 1u << 9, "Field"),
        new UnlockableContent("Quantum Field", 1u << 10, "Field"),
        new UnlockableContent("Darts minigame", 1u << 11, "Minigame"),
        new UnlockableContent("Aquadome (all progress flags)", 0xF000, "Field")
    };
}
