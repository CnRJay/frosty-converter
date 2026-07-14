using FrostyConvert.Core.FifaMod;
using FrostyConvert.Core.Project;

namespace FrostyConvert.Tests;

public class FifamodReaderTests
{
    [Fact]
    public void IsFifamod_False_ForEmptyStream()
    {
        using var ms = new MemoryStream(new byte[] { 0, 1, 2, 3 });
        Assert.False(FifamodReader.IsFifamod(ms));
    }

    [Fact]
    public void IsFifamod_True_ForFetmMagic()
    {
        using var ms = new MemoryStream("FETM"u8.ToArray());
        Assert.True(FifamodReader.IsFifamod(ms));
    }

    [Fact]
    public void Read_Throws_WhenNotFetm()
    {
        using var ms = new MemoryStream(new byte[] { 0x58, 0x58, 0x58, 0x58, 0, 0, 0, 0, 0, 0, 0, 0 });
        var ex = Assert.Throws<FifamodReaderException>(() =>
            FifamodReader.Read(ms, "bad.fifamod", loadResourceData: false, decompress: false));
        Assert.Contains("FETM", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FifaprojectWriter_MagicConstant()
    {
        Assert.Equal(0x50544546u, FifaprojectWriter.MagicLe);
        Assert.Equal(2, FifaprojectWriter.ProjectVersion);
    }
}
