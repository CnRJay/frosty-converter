using FrostyConvert.Core.Convert;
using FrostyConvert.Core.Mod;
using FrostyConvert.Core.Project;

namespace FrostyConvert.Tests;

public class FbprojectReaderTests
{
    [Fact]
    public void RoundTrip_SyntheticProject_HasNoCustomHandlers()
    {
        byte[] modBytes = FbmodWriterHelper.BuildMinimalV5(
            "CollegeFB27",
            42,
            new FbmodDetails
            {
                Title = "T",
                Author = "A",
                Category = "C",
                Version = "1",
                Description = "D",
                Link = "",
            },
            new[]
            {
                new SyntheticResource
                {
                    Type = ModResourceType.Ebx,
                    Name = "football/test",
                    Data = BuildUncompressedCas("RIFF"u8.ToArray().Concat(new byte[] { 1, 2, 3, 4 }).ToArray()),
                },
            });

        using var ms = new MemoryStream(modBytes);
        var mod = FbmodReader.Read(ms, "t.fbmod");
        var (project, report) = ModToProjectConverter.Convert(mod);
        Assert.True(report.Success, string.Join("; ", report.Errors));

        using var outMs = new MemoryStream();
        FbprojectWriter.Write(outMs, project);
        outMs.Position = 0;
        var summary = FbprojectReader.ReadSummary(outMs);

        Assert.Equal("CollegeFB27", summary.ProfileName);
        Assert.Single(summary.ModifiedEbx);
        Assert.False(summary.ModifiedEbx[0].IsCustomHandler);
        Assert.Equal("RIFF", summary.ModifiedEbx[0].DataMagic);
    }

    private static byte[] BuildUncompressedCas(byte[] raw)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        int size = raw.Length;
        w.Write((byte)((size >> 24) & 0xFF));
        w.Write((byte)((size >> 16) & 0xFF));
        w.Write((byte)((size >> 8) & 0xFF));
        w.Write((byte)(size & 0xFF));
        w.Write((byte)0x00);
        w.Write((byte)0x70);
        w.Write((byte)((size >> 8) & 0xFF));
        w.Write((byte)(size & 0xFF));
        w.Write(raw);
        return ms.ToArray();
    }
}
