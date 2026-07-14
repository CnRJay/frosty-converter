using FrostyConvert.Core.IO;

namespace FrostyConvert.Core.Project;

/// <summary>
/// Lightweight reader for validating .fbproject v14 files we write (header + ebx inventory).
/// Not a full project loader — only enough to sanity-check offline recovery output.
/// </summary>
public static class FbprojectReader
{
    public sealed class Summary
    {
        public uint Version { get; init; }
        public string ProfileName { get; init; } = "";
        public uint GameVersion { get; init; }
        public string Title { get; init; } = "";
        public string Author { get; init; } = "";
        public int AddedBundleCount { get; init; }
        public int AddedEbxCount { get; init; }
        public int AddedResCount { get; init; }
        public int AddedChunkCount { get; init; }
        public int ModifiedEbxCount { get; set; }
        public int ModifiedResCount { get; set; }
        public int ModifiedChunkCount { get; set; }
        public List<EbxSummary> ModifiedEbx { get; } = new();
    }

    public sealed class EbxSummary
    {
        public required string Name { get; init; }
        public bool HasModifiedData { get; init; }
        public bool IsCustomHandler { get; init; }
        public int DataLength { get; init; }
        public string DataMagic { get; init; } = "";
    }

    public static Summary ReadSummary(string path)
    {
        using var fs = File.OpenRead(path);
        return ReadSummary(fs);
    }

    public static Summary ReadSummary(Stream stream)
    {
        using var r = new EndianBinaryReader(stream, leaveOpen: true);

        ulong magic = r.ReadUInt64();
        if (magic != FbprojectConstants.Magic)
            throw new InvalidDataException($"Not a binary .fbproject (magic 0x{magic:X16}).");

        uint version = r.ReadUInt32();
        string profile = r.ReadNullTerminatedString();
        _ = r.ReadInt64(); // creation
        _ = r.ReadInt64(); // modified
        uint gameVersion = r.ReadUInt32();

        string title = r.ReadNullTerminatedString();
        string author = r.ReadNullTerminatedString();
        _ = r.ReadNullTerminatedString(); // category
        _ = r.ReadNullTerminatedString(); // version
        _ = r.ReadNullTerminatedString(); // description

        SkipBuffer(r); // icon
        for (int i = 0; i < 4; i++)
            SkipBuffer(r);

        int superBundles = r.ReadInt32();
        for (int i = 0; i < superBundles; i++)
        { /* unused in stock */ }

        int addedBundles = r.ReadInt32();
        for (int i = 0; i < addedBundles; i++)
        {
            _ = r.ReadNullTerminatedString();
            _ = r.ReadNullTerminatedString();
            _ = r.ReadInt32();
        }

        int addedEbx = r.ReadInt32();
        for (int i = 0; i < addedEbx; i++)
        {
            _ = r.ReadNullTerminatedString();
            _ = r.ReadGuid();
        }

        int addedRes = r.ReadInt32();
        for (int i = 0; i < addedRes; i++)
        {
            _ = r.ReadNullTerminatedString();
            _ = r.ReadUInt64();
            _ = r.ReadUInt32();
            _ = r.ReadBytes(16);
        }

        int addedChunks = r.ReadInt32();
        for (int i = 0; i < addedChunks; i++)
        {
            _ = r.ReadGuid();
            _ = r.ReadInt32();
        }

        var summary = new Summary
        {
            Version = version,
            ProfileName = profile,
            GameVersion = gameVersion,
            Title = title,
            Author = author,
            AddedBundleCount = addedBundles,
            AddedEbxCount = addedEbx,
            AddedResCount = addedRes,
            AddedChunkCount = addedChunks,
        };

        int modEbx = r.ReadInt32();
        summary.ModifiedEbxCount = modEbx;
        for (int i = 0; i < modEbx; i++)
        {
            string name = r.ReadNullTerminatedString();
            SkipLinkedAssets(r);
            int bundleCount = r.ReadInt32();
            for (int j = 0; j < bundleCount; j++)
                _ = r.ReadNullTerminatedString();

            bool hasMod = r.ReadByte() != 0;
            bool custom = false;
            int dataLen = 0;
            string magicStr = "";
            if (hasMod)
            {
                _ = r.ReadByte(); // transient
                _ = r.ReadNullTerminatedString(); // userData
                custom = r.ReadByte() != 0;
                dataLen = r.ReadInt32();
                if (dataLen < 0 || dataLen > 512 * 1024 * 1024)
                    throw new InvalidDataException($"Corrupt ebx data length {dataLen} for '{name}'.");
                byte[] data = r.ReadBytes(dataLen);
                if (data.Length >= 4)
                    magicStr = System.Text.Encoding.ASCII.GetString(data, 0, 4);
            }

            summary.ModifiedEbx.Add(new EbxSummary
            {
                Name = name,
                HasModifiedData = hasMod,
                IsCustomHandler = custom,
                DataLength = dataLen,
                DataMagic = magicStr,
            });
        }

        return summary;
    }

    private static void SkipBuffer(EndianBinaryReader r)
    {
        int len = r.ReadInt32();
        if (len > 0)
            r.ReadBytes(len);
    }

    private static void SkipLinkedAssets(EndianBinaryReader r)
    {
        int count = r.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            string type = r.ReadNullTerminatedString();
            if (string.Equals(type, "chunk", StringComparison.OrdinalIgnoreCase))
                _ = r.ReadGuid();
            else
                _ = r.ReadNullTerminatedString();
        }
    }
}
