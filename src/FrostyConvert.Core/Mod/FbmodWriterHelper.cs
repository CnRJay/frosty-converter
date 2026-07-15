using System.Text;

namespace FrostyConvert.Core.Mod;

/// <summary>
/// Minimal binary .fbmod writer used only for synthetic test fixtures.
/// Matches Frosty binary layout for version 5 (enough for parser tests).
/// </summary>
public static class FbmodWriterHelper
{
    public static byte[] BuildMinimalV5(
        string profileName,
        int gameVersion,
        FbmodDetails details,
        IReadOnlyList<SyntheticResource> resources)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);

        var payloads = new List<byte[]>();
        foreach (var r in resources)
        {
            if (r.Data is { Length: > 0 })
            {
                r.AssignedIndex = payloads.Count;
                payloads.Add(r.Data);
            }
            else
            {
                r.AssignedIndex = -1;
            }
        }

        w.Write(FbmodConstants.BinaryMagic);
        w.Write(5u);
        long headerDataOffsetPos = ms.Position;
        w.Write(0L); // placeholder dataOffset
        w.Write(0);  // placeholder dataCount

        // BinaryWriter.Write(string) — same as Frosty profile encoding
        w.Write(profileName);
        w.Write(gameVersion);

        WriteNullTerminated(w, details.Title);
        WriteNullTerminated(w, details.Author);
        WriteNullTerminated(w, details.Category);
        WriteNullTerminated(w, details.Version);
        WriteNullTerminated(w, details.Description);
        WriteNullTerminated(w, details.Link ?? "");

        w.Write(resources.Count);
        foreach (var r in resources)
            WriteResource(w, r);

        long dataOffset = ms.Position;

        long currentOffset = 0;
        var table = new List<(long offset, long size)>();
        foreach (var p in payloads)
        {
            table.Add((currentOffset, p.Length));
            currentOffset += p.Length;
        }

        foreach (var (offset, size) in table)
        {
            w.Write(offset);
            w.Write(size);
        }

        foreach (var p in payloads)
            w.Write(p);

        long end = ms.Position;
        ms.Position = headerDataOffsetPos;
        w.Write(dataOffset);
        w.Write(payloads.Count);
        ms.Position = end;

        return ms.ToArray();
    }

    private static void WriteResource(BinaryWriter w, SyntheticResource r)
    {
        w.Write((byte)r.Type);
        w.Write(r.AssignedIndex);
        WriteNullTerminated(w, r.Name);

        if (r.AssignedIndex != -1)
        {
            byte[] sha1 = r.Sha1 ?? new byte[20];
            if (sha1.Length != 20)
                Array.Resize(ref sha1, 20);
            w.Write(sha1);
            w.Write(r.OriginalSize != 0 ? r.OriginalSize : r.Data?.LongLength ?? 0);
            w.Write(r.Flags);
            w.Write(r.HandlerHash);
            WriteNullTerminated(w, r.UserData ?? "");
        }

        w.Write(r.AddedBundleHashes?.Count ?? 0);
        if (r.AddedBundleHashes is not null)
        {
            foreach (int h in r.AddedBundleHashes)
                w.Write(h);
        }

        switch (r.Type)
        {
            case ModResourceType.Res:
                w.Write(r.ResType);
                w.Write(r.ResRid);
                byte[] meta = r.ResMeta ?? Array.Empty<byte>();
                w.Write(meta.Length);
                if (meta.Length > 0)
                    w.Write(meta);
                break;
            case ModResourceType.Chunk:
                // Writer always emits open-Frosty / v5 chunk layout (no h64 / superBundles).
                // Production MMC mods are read by FbmodReader; synthetic fixtures stay v5.
                w.Write(r.RangeStart);
                w.Write(r.RangeEnd);
                w.Write(r.LogicalOffset);
                w.Write(r.LogicalSize);
                w.Write(r.H32);
                w.Write(r.FirstMip);
                break;
            case ModResourceType.Bundle:
                // BundleResource.Write re-emits name + superbundle hash after base fields
                WriteNullTerminated(w, r.Name);
                w.Write(r.SuperBundleHash);
                break;
        }
    }

    private static void WriteNullTerminated(BinaryWriter w, string s)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(s ?? "");
        w.Write(bytes);
        w.Write((byte)0);
    }
}

public sealed class SyntheticResource
{
    public ModResourceType Type { get; set; }
    public string Name { get; set; } = "";
    public byte[]? Data { get; set; }
    public byte[]? Sha1 { get; set; }
    public long OriginalSize { get; set; }
    public byte Flags { get; set; }
    public int HandlerHash { get; set; }
    public string? UserData { get; set; }
    public List<int>? AddedBundleHashes { get; set; }
    public uint ResType { get; set; }
    public ulong ResRid { get; set; }
    public byte[]? ResMeta { get; set; }
    public uint RangeStart { get; set; }
    public uint RangeEnd { get; set; }
    public uint LogicalOffset { get; set; }
    public uint LogicalSize { get; set; }
    public int H32 { get; set; }
    public int FirstMip { get; set; } = -1;
    public int SuperBundleHash { get; set; }

    internal int AssignedIndex { get; set; } = -1;
}
