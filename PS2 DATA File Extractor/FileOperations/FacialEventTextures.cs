namespace PS2_DATA_File_Extractor.FileOperations;

public sealed class FacialEventTextureSet
{
    public FacialEventTextureSet(
        string characterCode,
        string characterName,
        IReadOnlyDictionary<int, FacialEventTexture> eyes,
        IReadOnlyDictionary<int, FacialEventTexture> mouths)
    {
        CharacterCode = characterCode;
        CharacterName = characterName;
        Eyes = eyes;
        Mouths = mouths;
    }

    public string CharacterCode { get; }
    public string CharacterName { get; }
    public IReadOnlyDictionary<int, FacialEventTexture> Eyes { get; }
    public IReadOnlyDictionary<int, FacialEventTexture> Mouths { get; }

    public bool TryGetEyes(int pose, out FacialEventTexture texture) =>
        Eyes.TryGetValue(pose, out texture!);

    public bool TryGetMouth(int pose, out FacialEventTexture texture) =>
        Mouths.TryGetValue(pose, out texture!);
}

public sealed record FacialEventTexture(string SourcePath, int Pose, byte[] Data);
