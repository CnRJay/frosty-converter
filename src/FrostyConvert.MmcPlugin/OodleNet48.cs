using System;
using System.IO;
using System.Runtime.InteropServices;

namespace FrostyConvert.MmcPlugin;

/// <summary>net48 LoadLibrary-based Oodle bind (mirrors Core Oodle).</summary>
public static class OodleNet48
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int DecompressFunc(
        IntPtr src, long srcSize, IntPtr dst, long dstSize,
        int a5, int a6, long a7, long a8, long a9, long a10, long a11, long a12, long a13, int a14);

    private static DecompressFunc? _decompress;
    private static string? _path;

    public static bool IsBound
    {
        get
        {
            Ensure();
            return _decompress != null;
        }
    }

    public static string? BoundPath
    {
        get
        {
            Ensure();
            return _path;
        }
    }

    public static void TryBindSearch(params string?[] dirs)
    {
        if (_decompress != null) return;
        string[] names =
        {
            "oodle-data-shared.dll", "oo2core_win64.dll", "oo2core_9_win64.dll",
            "oo2core_8_win64.dll", "oo2core_7_win64.dll", "oo2core_6_win64.dll",
        };
        foreach (string? dir in dirs)
        {
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) continue;
            foreach (string name in names)
            {
                if (TryBind(Path.Combine(dir, name)))
                    return;
            }
        }
    }

    public static bool TryBind(string path)
    {
        if (!File.Exists(path)) return false;
        IntPtr h = LoadLibrary(path);
        if (h == IntPtr.Zero) return false;
        IntPtr p = GetProcAddress(h, "OodleLZ_Decompress");
        if (p == IntPtr.Zero) return false;
        _decompress = Marshal.GetDelegateForFunctionPointer<DecompressFunc>(p);
        _path = Path.GetFullPath(path);
        return true;
    }

    public static byte[] Decompress(byte[] compressed, int decompressedSize)
    {
        Ensure();
        if (_decompress == null)
            throw new CasDecompressException("Oodle DLL not loaded (expected oodle-data-shared.dll next to MMCEditor.exe).");

        var output = new byte[decompressedSize];
        var src = GCHandle.Alloc(compressed, GCHandleType.Pinned);
        var dst = GCHandle.Alloc(output, GCHandleType.Pinned);
        try
        {
            int r = _decompress(src.AddrOfPinnedObject(), compressed.Length, dst.AddrOfPinnedObject(), decompressedSize,
                0, 0, 0, 0, 0, 0, 0, 0, 0, 3);
            if (r <= 0) throw new CasDecompressException($"OodleLZ_Decompress returned {r}");
            if (r == decompressedSize) return output;
            var t = new byte[r];
            Buffer.BlockCopy(output, 0, t, 0, r);
            return t;
        }
        finally
        {
            src.Free();
            dst.Free();
        }
    }

    private static void Ensure()
    {
        if (_decompress != null) return;
        TryBindSearch(
            AppDomain.CurrentDomain.BaseDirectory,
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins"));
    }

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibrary(string path);

    [DllImport("kernel32", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr h, string name);
}
