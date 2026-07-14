using System.Runtime.InteropServices;

namespace FrostyConvert.Core.Compression;

/// <summary>
/// Binding to Oodle data compression (OodleLZ_Decompress).
/// Prefers the UE-distributed <c>oodle-data-shared.dll</c> (WorkingRobot/OodleUE builds),
/// then falls back to game <c>oo2core*_win64.dll</c>.
/// </summary>
public static class Oodle
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int DecompressFunc(
        IntPtr srcBuffer, long srcSize,
        IntPtr dstBuffer, long dstSize,
        int a5, int a6, long a7, long a8, long a9, long a10, long a11, long a12, long a13, int a14);

    private static DecompressFunc? _decompress;
    private static IntPtr _module = IntPtr.Zero;
    private static string? _boundPath;
    private static bool _searchAttempted;
    private static readonly object Gate = new();

    private static readonly string[] DllNames =
    {
        "oodle-data-shared.dll",
        "oo2core_win64.dll",
        "oo2core_9_win64.dll",
        "oo2core_8_win64.dll",
        "oo2core_7_win64.dll",
        "oo2core_6_win64.dll",
        "oo2core_5_win64.dll",
    };

    public static bool IsBound
    {
        get
        {
            EnsureDefaultBound();
            return _decompress is not null;
        }
    }

    public static string? BoundPath
    {
        get
        {
            EnsureDefaultBound();
            return _boundPath;
        }
    }

    /// <summary>Try to bind a specific DLL path.</summary>
    public static bool TryBind(string dllPath)
    {
        if (string.IsNullOrWhiteSpace(dllPath) || !File.Exists(dllPath))
            return false;

        lock (Gate)
        {
            string full = Path.GetFullPath(dllPath);
            if (_decompress is not null &&
                string.Equals(_boundPath, full, StringComparison.OrdinalIgnoreCase))
                return true;

            if (!NativeLibrary.TryLoad(full, out IntPtr module))
                return false;

            if (!NativeLibrary.TryGetExport(module, "OodleLZ_Decompress", out IntPtr proc) || proc == IntPtr.Zero)
            {
                NativeLibrary.Free(module);
                return false;
            }

            if (_module != IntPtr.Zero && _module != module)
            {
                try { NativeLibrary.Free(_module); } catch { /* ignore */ }
            }

            _module = module;
            _boundPath = full;
            _decompress = Marshal.GetDelegateForFunctionPointer<DecompressFunc>(proc);
            return true;
        }
    }

    public static bool TryBindFromSearchPaths(IEnumerable<string?> candidateDirs)
    {
        if (_decompress is not null)
            return true;

        foreach (string? dir in candidateDirs)
        {
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                continue;

            foreach (string name in DllNames)
            {
                if (TryBind(Path.Combine(dir, name)))
                    return true;
            }
        }

        return _decompress is not null;
    }

    public static byte[] Decompress(byte[] compressed, int decompressedSize)
    {
        EnsureDefaultBound();
        if (_decompress is null)
            throw new CasDecompressException(
                "Oodle is not available. Expected oodle-data-shared.dll next to the tool " +
                "(from third_party/oodle) or pass --oodle path\\to\\oo2core_win64.dll.");

        var output = new byte[decompressedSize];
        GCHandle src = GCHandle.Alloc(compressed, GCHandleType.Pinned);
        GCHandle dst = GCHandle.Alloc(output, GCHandleType.Pinned);
        try
        {
            int result = _decompress(
                src.AddrOfPinnedObject(), compressed.Length,
                dst.AddrOfPinnedObject(), decompressedSize,
                0, 0, 0, 0, 0, 0, 0, 0, 0, 3);

            if (result <= 0)
                throw new CasDecompressException($"OodleLZ_Decompress returned {result}.");
            if (result != decompressedSize)
            {
                var trimmed = new byte[result];
                Buffer.BlockCopy(output, 0, trimmed, 0, result);
                return trimmed;
            }
            return output;
        }
        finally
        {
            src.Free();
            dst.Free();
        }
    }

    private static void EnsureDefaultBound()
    {
        if (_decompress is not null || _searchAttempted)
            return;

        lock (Gate)
        {
            if (_decompress is not null || _searchAttempted)
                return;

            _searchAttempted = true;

            var dirs = new List<string?>
            {
                AppContext.BaseDirectory,
                Environment.CurrentDirectory,
            };

            string? walk = AppContext.BaseDirectory;
            for (int i = 0; i < 8 && walk is not null; i++)
            {
                dirs.Add(walk);
                dirs.Add(Path.Combine(walk, "third_party", "oodle", "bin"));
                dirs.Add(Path.Combine(walk, "native"));
                walk = Directory.GetParent(walk)?.FullName;
            }

            TryBindFromSearchPaths(dirs);
        }
    }
}
