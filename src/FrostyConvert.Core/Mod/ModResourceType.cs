namespace FrostyConvert.Core.Mod;

/// <summary>Matches Frosty <c>ModResourceType</c> (MMC / open Frosty).</summary>
public enum ModResourceType : byte
{
    Embedded = 0,
    Ebx = 1,
    Res = 2,
    Chunk = 3,
    Bundle = 4,
    /// <summary>Filesystem / initfs-style resource (rare; base fields only).</summary>
    FsFile = 5,
}
