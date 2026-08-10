using PS2_DATA_File_Extractor.FileOperations;

namespace PS2_DATA_File_Extractor.Tests;

public sealed class HexDataCodecTests
{
    [Fact]
    public void FormatAndParseRoundTripEveryByteValue()
    {
        byte[] expected = Enumerable.Range(0, 256).Select(value => (byte)value).ToArray();

        string formatted = HexDataCodec.Format(expected);
        bool parsed = HexDataCodec.TryParse(formatted, out byte[] actual, out string error);

        Assert.True(parsed, error);
        Assert.Equal(expected, actual);
        Assert.Equal(16, formatted.Split(Environment.NewLine)[0].Split(' ').Length);
    }

    [Fact]
    public void ParserAcceptsPrefixesSeparatorsAndContiguousPairs()
    {
        Assert.True(HexDataCodec.TryParse("0x00, 7f;80 FF\n1234", out byte[] data, out string error), error);
        Assert.Equal(new byte[] { 0x00, 0x7F, 0x80, 0xFF, 0x12, 0x34 }, data);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("GG")]
    [InlineData("123")]
    public void ParserRejectsInvalidHex(string text)
    {
        Assert.False(HexDataCodec.TryParse(text, out byte[] data, out string error));
        Assert.Empty(data);
        Assert.NotEmpty(error);
    }
}
