using FrostyConvert.Core.IO;

namespace FrostyConvert.Core.Project;

/// <summary>
/// Offline validator for FETP v2 projects (layout from <c>Fifa_Tool.EditorProject.Load</c>).
/// Counts assets without needing a live game / AssetManager.
/// </summary>
public static class FifaprojectReader
{
    public sealed class Summary
    {
        public byte ProjectVersion { get; set; }
        public string GameName { get; set; } = "";
        public uint GameVersion { get; set; }
        public string Title { get; set; } = "";
        public int AddedBundleCount { get; set; }
        public int ChunkCount { get; set; }
        public int LegacyChunkCount { get; set; }
        public int LegacyAddedCount { get; set; }
        public int ResCount { get; set; }
        public int EbxCount { get; set; }
        public List<string> SampleLegacyPaths { get; } = new();
        public List<string> SampleResNames { get; } = new();
        public List<string> SampleEbxNames { get; } = new();
        public List<string> Warnings { get; } = new();
    }

    public static Summary ReadSummary(string path)
    {
        using var fs = File.OpenRead(path);
        return ReadSummary(fs);
    }

    public static Summary ReadSummary(Stream stream)
    {
        using var r = new EndianBinaryReader(stream, leaveOpen: true);
        var s = new Summary();

        uint magic = r.ReadUInt32();
        if (magic != FifaprojectWriter.MagicLe)
            throw new InvalidDataException($"Not a .fifaproject (expected FETP, got 0x{magic:X8}).");

        s.ProjectVersion = r.ReadByte();
        _ = r.ReadByte(); // tool major
        _ = r.ReadByte();
        _ = r.ReadByte();
        _ = r.ReadByte();

        s.GameName = r.ReadLengthPrefixedString();
        s.GameVersion = ReadUInt24(r);

        // Mod settings (same order as FifaprojectWriter.WriteHeader)
        s.Title = r.ReadLengthPrefixedString();
        _ = r.ReadLengthPrefixedString(); // author
        _ = r.ReadByte(); // main cat
        _ = r.ReadByte(); // sub
        _ = r.ReadLengthPrefixedString(); // custom
        _ = r.ReadLengthPrefixedString(); // second custom
        _ = r.ReadLengthPrefixedString(); // version
        _ = r.ReadLengthPrefixedString(); // desc
        for (int i = 0; i < 8; i++)
            _ = r.ReadLengthPrefixedString(); // links

        int iconLen = r.Read7BitEncodedInt();
        if (iconLen > 0)
            r.BaseStream.Position += iconLen;

        int shots = r.Read7BitEncodedInt();
        for (int i = 0; i < shots; i++)
        {
            int n = r.Read7BitEncodedInt();
            r.BaseStream.Position += n;
        }

        // locale, initfs, player lua, player kit lua — our writer always writes count 0
        for (int i = 0; i < 4; i++)
        {
            int c = r.Read7BitEncodedInt();
            if (c != 0)
                s.Warnings.Add($"Expected empty header table #{i}, got count={c} (parser may desync).");
        }

        s.AddedBundleCount = (int)ReadUInt24(r);
        for (int i = 0; i < s.AddedBundleCount; i++)
        {
            _ = r.ReadLengthPrefixedString();
            _ = r.ReadUInt32();
            _ = r.ReadByte();
        }

        // Chunks — field order from EditorProject.Load
        s.ChunkCount = (int)ReadUInt24(r);
        for (int i = 0; i < s.ChunkCount; i++)
        {
            Guid id = r.ReadGuid();
            ushort flags = r.ReadUInt16();
            bool isAdded = (flags & 1) != 0;
            _ = r.ReadSha1();

            if ((flags & 0x08) != 0)
                _ = r.Read7BitEncodedInt();
            if ((flags & 0x10) != 0)
                _ = r.Read7BitEncodedInt();
            if ((flags & 0x20) != 0)
            {
                bool h32IsU64 = s.GameName.Contains("26", StringComparison.Ordinal);
                if (h32IsU64)
                    _ = r.ReadUInt64();
                else
                    _ = r.ReadUInt32();
            }

            int dataLen = r.Read7BitEncodedInt();
            if (dataLen < 0 || dataLen > stream.Length)
                throw new InvalidDataException($"Chunk {id}: invalid data length {dataLen} at index {i}.");

            if ((flags & 0x02) != 0) // IsLegacy
            {
                s.LegacyChunkCount++;
                ulong hash = r.ReadUInt64();
                string? path = null;
                if ((flags & 0x40) != 0) // IsLegacyAdded
                {
                    s.LegacyAddedCount++;
                    path = r.ReadLengthPrefixedString();
                }
                if (s.SampleLegacyPaths.Count < 12)
                {
                    s.SampleLegacyPaths.Add(path is not null
                        ? path
                        : $"(hash 0x{hash:x16})");
                }
            }

            _ = ReadUInt24(r);
            if (!isAdded)
                _ = r.ReadSha1();

            if ((flags & 0x80) != 0)
            {
                int n = r.Read7BitEncodedInt();
                for (int b = 0; b < n; b++)
                    _ = r.ReadUInt64();
            }

            if ((flags & 0x04) != 0)
                _ = r.ReadUInt32();

            _ = r.ReadByte();
            r.BaseStream.Position += dataLen;
        }

        // Res
        s.ResCount = (int)ReadUInt24(r);
        for (int i = 0; i < s.ResCount; i++)
        {
            string name = r.ReadLengthPrefixedString();
            byte resFlags = r.ReadByte();
            bool isAdded = (resFlags & 1) != 0;
            bool directly = (resFlags & 2) != 0;
            bool hasLinked = (resFlags & 8) != 0;

            if (s.SampleResNames.Count < 12)
                s.SampleResNames.Add(name);

            if (!directly)
            {
                if (hasLinked && s.ProjectVersion >= 2)
                    SkipLinkedAssets(r);
                continue;
            }

            if (isAdded)
            {
                _ = r.ReadUInt32();
                _ = r.ReadUInt64();
            }

            _ = r.ReadSha1();
            _ = r.Read7BitEncodedInt();
            if ((resFlags & 4) != 0)
                r.BaseStream.Position += 16;

            _ = ReadUInt24(r);
            if (!isAdded)
                _ = r.ReadSha1();

            if ((resFlags & 0x10) != 0)
            {
                int n = r.Read7BitEncodedInt();
                for (int b = 0; b < n; b++)
                    _ = r.ReadUInt64();
            }

            _ = r.ReadByte();
            int dataLen = r.Read7BitEncodedInt();
            r.BaseStream.Position += dataLen;

            if (hasLinked && s.ProjectVersion >= 2)
                SkipLinkedAssets(r);
        }

        // EBX
        s.EbxCount = (int)ReadUInt24(r);
        for (int i = 0; i < s.EbxCount; i++)
        {
            string name = r.ReadLengthPrefixedString();
            byte ebxFlags = r.ReadByte();
            bool isAdded = (ebxFlags & 1) != 0;
            bool directly = (ebxFlags & 2) != 0;
            bool hasLinked = (ebxFlags & 8) != 0;
            bool hasBrt = (ebxFlags & 4) != 0;
            bool hasParent = (ebxFlags & 0x40) != 0;

            if (s.SampleEbxNames.Count < 12)
                s.SampleEbxNames.Add(name);

            if (!directly)
            {
                if (hasLinked && s.ProjectVersion >= 2)
                    SkipLinkedAssets(r);
                continue;
            }

            if (isAdded)
            {
                _ = r.ReadLengthPrefixedString();
                _ = r.ReadGuid();
            }

            _ = ReadUInt24(r);
            if (!isAdded)
                _ = r.ReadSha1();

            if (hasBrt)
            {
                _ = r.ReadUInt32();
                _ = r.ReadLengthPrefixedString();
                if (hasParent)
                    _ = r.ReadLengthPrefixedString();
            }

            if ((ebxFlags & 0x10) != 0)
            {
                int n = r.Read7BitEncodedInt();
                for (int b = 0; b < n; b++)
                    _ = r.ReadUInt64();
            }

            _ = r.ReadSha1();
            _ = r.Read7BitEncodedInt();
            _ = r.ReadByte();
            int dataLen = r.Read7BitEncodedInt();
            r.BaseStream.Position += dataLen;

            if (hasLinked && s.ProjectVersion >= 2)
                SkipLinkedAssets(r);
        }

        long remaining = stream.Length - stream.Position;
        if (remaining != 0)
            s.Warnings.Add($"Trailing bytes after parse: {remaining} (possible layout mismatch).");

        return s;
    }

    private static void SkipLinkedAssets(EndianBinaryReader r)
    {
        int n = r.Read7BitEncodedInt();
        if (n != 0)
            throw new InvalidDataException("Linked assets present; offline skip not fully implemented.");
    }

    private static uint ReadUInt24(EndianBinaryReader r)
    {
        uint b0 = r.ReadByte();
        uint b1 = r.ReadByte();
        uint b2 = r.ReadByte();
        return b0 | (b1 << 8) | (b2 << 16);
    }
}
