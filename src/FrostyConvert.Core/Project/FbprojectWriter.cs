using FrostyConvert.Core.IO;

namespace FrostyConvert.Core.Project;

/// <summary>
/// Writes Frosty <c>.fbproject</c> format version 14 (binary).
/// </summary>
public static class FbprojectWriter
{
    public static void Write(string path, ProjectDocument project)
    {
        string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        string temp = path + ".tmp";
        using (var fs = File.Create(temp))
            Write(fs, project);

        if (File.Exists(path))
            File.Delete(path);
        File.Move(temp, path);
    }

    public static void Write(Stream stream, ProjectDocument project)
    {
        using var w = new EndianBinaryWriter(stream, leaveOpen: true);

        w.Write(FbprojectConstants.Magic);
        w.Write(project.Version);
        w.WriteNullTerminatedString(project.ProfileName);
        w.Write(project.CreationDate.Ticks);
        w.Write(project.ModifiedDate.Ticks);
        w.Write(project.GameVersion);

        w.WriteNullTerminatedString(project.Title);
        w.WriteNullTerminatedString(project.Author);
        w.WriteNullTerminatedString(project.Category);
        w.WriteNullTerminatedString(project.ModVersion);
        w.WriteNullTerminatedString(project.Description);

        WriteBuffer(w, project.Icon);
        for (int i = 0; i < 4; i++)
            WriteBuffer(w, project.Screenshots.Length > i ? project.Screenshots[i] : null);

        // superbundles (unused)
        w.Write(0);

        // added bundles
        WriteCountPrefixed(w, project.AddedBundles, (bw, b) =>
        {
            bw.WriteNullTerminatedString(b.Name);
            bw.WriteNullTerminatedString(b.SuperBundleName);
            bw.Write(b.Type);
        });

        // added ebx
        WriteCountPrefixed(w, project.AddedEbx, (bw, e) =>
        {
            bw.WriteNullTerminatedString(e.Name);
            bw.Write(e.Guid);
        });

        // added res
        WriteCountPrefixed(w, project.AddedRes, (bw, r) =>
        {
            bw.WriteNullTerminatedString(r.Name);
            bw.Write(r.ResRid);
            bw.Write(r.ResType);
            byte[] meta = r.ResMeta.Length >= 16 ? r.ResMeta : PadMeta(r.ResMeta);
            bw.Write(meta, 0, 16);
        });

        // added chunks
        WriteCountPrefixed(w, project.AddedChunks, (bw, c) =>
        {
            bw.Write(c.Id);
            bw.Write(c.H32);
        });

        // modified ebx
        WriteCountPrefixed(w, project.ModifiedEbx, WriteModifiedEbx);

        // modified res
        WriteCountPrefixed(w, project.ModifiedRes, WriteModifiedRes);

        // modified chunks
        WriteCountPrefixed(w, project.ModifiedChunks, WriteModifiedChunk);

        // custom actions (legacy section — always write count 1 with type "legacy")
        long customCountPos = w.Position;
        w.Write(0xDEADBEEF);
        w.WriteNullTerminatedString("legacy");
        long legacyCountPos = w.Position;
        w.Write(0xDEADBEEF);
        foreach (var leg in project.LegacyEntries)
        {
            w.WriteNullTerminatedString(leg.Name);
            WriteLinkedAssets(w, leg.LinkedAssets);
            w.Write(leg.ChunkId);
            w.Write(leg.Offset);
            w.Write(leg.CompressedOffset);
            w.Write(leg.CompressedSize);
            w.Write(leg.Size);
        }
        long end = w.Position;
        w.Position = legacyCountPos;
        w.Write(project.LegacyEntries.Count);
        w.Position = customCountPos;
        w.Write(1); // one custom handler block ("legacy")
        w.Position = end;
    }

    private static void WriteModifiedEbx(EndianBinaryWriter w, ProjectEbxModified e)
    {
        w.WriteNullTerminatedString(e.Name);
        WriteLinkedAssets(w, e.LinkedAssets);
        w.Write(e.AddedBundleNames.Count);
        foreach (string b in e.AddedBundleNames)
            w.WriteNullTerminatedString(b);

        w.Write(e.HasModifiedData);
        if (!e.HasModifiedData)
            return;

        w.Write(e.IsTransientModified);
        w.WriteNullTerminatedString(e.UserData);
        w.Write(e.IsCustomHandler);
        byte[] data = e.Data ?? Array.Empty<byte>();
        w.Write(data.Length);
        w.Write(data);
    }

    private static void WriteModifiedRes(EndianBinaryWriter w, ProjectResModified r)
    {
        w.WriteNullTerminatedString(r.Name);
        WriteLinkedAssets(w, r.LinkedAssets);
        w.Write(r.AddedBundleNames.Count);
        foreach (string b in r.AddedBundleNames)
            w.WriteNullTerminatedString(b);

        w.Write(r.HasModifiedData);
        if (!r.HasModifiedData)
            return;

        w.WriteSha1(r.Sha1);
        w.Write(r.OriginalSize);
        if (r.ResMeta is { Length: > 0 })
        {
            w.Write(r.ResMeta.Length);
            w.Write(r.ResMeta);
        }
        else
        {
            w.Write(0);
        }

        w.WriteNullTerminatedString(r.UserData);
        byte[] data = r.Data ?? Array.Empty<byte>();
        w.Write(data.Length);
        w.Write(data);
    }

    private static void WriteModifiedChunk(EndianBinaryWriter w, ProjectChunkModified c)
    {
        w.Write(c.Id);
        w.Write(c.AddedBundleNames.Count);
        foreach (string b in c.AddedBundleNames)
            w.WriteNullTerminatedString(b);

        w.Write(c.FirstMip);
        w.Write(c.H32);
        w.Write(c.HasModifiedData);
        if (!c.HasModifiedData)
            return;

        w.WriteSha1(c.Sha1);
        w.Write(c.LogicalOffset);
        w.Write(c.LogicalSize);
        w.Write(c.RangeStart);
        w.Write(c.RangeEnd);
        w.Write(c.AddToChunkBundle);
        w.WriteNullTerminatedString(c.UserData);
        byte[] data = c.Data ?? Array.Empty<byte>();
        w.Write(data.Length);
        w.Write(data);
    }

    private static void WriteLinkedAssets(EndianBinaryWriter w, List<ProjectLinkedAsset> links)
    {
        w.Write(links.Count);
        foreach (var link in links)
        {
            w.WriteNullTerminatedString(link.AssetType);
            if (string.Equals(link.AssetType, "chunk", StringComparison.OrdinalIgnoreCase))
                w.Write(link.ChunkId ?? Guid.Empty);
            else
                w.WriteNullTerminatedString(link.Name ?? "");
        }
    }

    private static void WriteBuffer(EndianBinaryWriter w, byte[]? buffer)
    {
        if (buffer is { Length: > 0 })
        {
            w.Write(buffer.Length);
            w.Write(buffer);
        }
        else
        {
            w.Write(0);
        }
    }

    private static void WriteCountPrefixed<T>(
        EndianBinaryWriter w,
        IReadOnlyList<T> items,
        Action<EndianBinaryWriter, T> writeItem)
    {
        long countPos = w.Position;
        w.Write(0xDEADBEEF);
        foreach (T item in items)
            writeItem(w, item);
        long end = w.Position;
        w.Position = countPos;
        w.Write(items.Count);
        w.Position = end;
    }

    private static byte[] PadMeta(byte[] meta)
    {
        var m = new byte[16];
        Buffer.BlockCopy(meta, 0, m, 0, Math.Min(16, meta.Length));
        return m;
    }
}
