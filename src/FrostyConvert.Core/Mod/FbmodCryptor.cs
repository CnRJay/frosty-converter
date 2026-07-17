using System.Security.Cryptography;
using System.Text;

namespace FrostyConvert.Core.Mod;

/// <summary>
/// AES-256-CBC + HMAC-SHA256 payload protection used by MMC Editor/Mod Manager 1.1.0.1+
/// (binary .fbmod version 8). Compatible with <c>Frosty.Core.IO.FrostyModCryptor</c>.
/// Plaintext blobs (no <c>FMENC001</c> header) pass through unchanged.
/// </summary>
public static class FbmodCryptor
{
    // ASCII bytes — avoid UTF-8 string literals so the same file compiles in the net48 plugin.
    private static readonly byte[] PayloadMagic = Encoding.ASCII.GetBytes("FMENC001");

    private const int IvSize = 16;
    private const int MacSize = 32;

    // Key material matches MMC FrostyModKeyProvider (obfuscated constants, then SHA-256).
    private static readonly byte[] PartA =
    {
        99, 17, 169, 66, 124, 94, 144, 212, 42, 143,
        23, 193, 85, 224, 59, 109,
    };

    private static readonly byte[] PartB =
    {
        16, 114, 204, 33, 29, 43, 245, 161, 73, 234,
        98, 167, 48, 149, 88, 12,
    };

    private static readonly byte[] Mask =
    {
        34, 68, 110, 8, 57, 119, 19, 90, 15, 156,
        113, 45, 102, 24, 179, 126,
    };

    public static bool IsEncrypted(byte[]? data)
    {
        if (data is null || data.Length < PayloadMagic.Length)
            return false;
        for (int i = 0; i < PayloadMagic.Length; i++)
        {
            if (data[i] != PayloadMagic[i])
                return false;
        }
        return true;
    }

    /// <summary>Decrypt if encrypted; otherwise return the same array.</summary>
    public static byte[] MaybeDecrypt(byte[] data)
    {
        if (!IsEncrypted(data))
            return data;
        return Decrypt(data);
    }

    public static byte[] Encrypt(byte[] data)
    {
        if (data is null || data.Length == 0)
            return data ?? Array.Empty<byte>();

        byte[]? encKey = null;
        byte[]? macKey = null;
        try
        {
            encKey = GetEncryptionKey();
            macKey = GetMacKey();

            byte[] iv = new byte[IvSize];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(iv);

            byte[] ciphertext;
            using (var aes = Aes.Create())
            {
                aes.KeySize = 256;
                aes.BlockSize = 128;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = encKey;
                aes.IV = iv;
                using ICryptoTransform encryptor = aes.CreateEncryptor();
                ciphertext = encryptor.TransformFinalBlock(data, 0, data.Length);
            }

            byte[] mac;
            using (var hmac = new HMACSHA256(macKey))
                mac = hmac.ComputeHash(Combine(PayloadMagic, iv, ciphertext));

            return Combine(PayloadMagic, iv, mac, ciphertext);
        }
        finally
        {
            Clear(encKey);
            Clear(macKey);
        }
    }

    public static byte[] Decrypt(byte[] data)
    {
        if (data is null || data.Length == 0)
            return data ?? Array.Empty<byte>();

        if (!IsEncrypted(data))
            return data;

        int headerLen = PayloadMagic.Length + IvSize + MacSize;
        if (data.Length < headerLen)
            throw new FbmodReaderException("Encrypted fbmod payload is truncated.");

        byte[]? encKey = null;
        byte[]? macKey = null;
        try
        {
            encKey = GetEncryptionKey();
            macKey = GetMacKey();

            int ivOff = PayloadMagic.Length;
            int macOff = ivOff + IvSize;
            int ctOff = macOff + MacSize;
            int ctLen = data.Length - ctOff;

            byte[] iv = new byte[IvSize];
            Buffer.BlockCopy(data, ivOff, iv, 0, IvSize);
            byte[] mac = new byte[MacSize];
            Buffer.BlockCopy(data, macOff, mac, 0, MacSize);
            byte[] ciphertext = new byte[ctLen];
            Buffer.BlockCopy(data, ctOff, ciphertext, 0, ctLen);

            byte[] expectedMac;
            using (var hmac = new HMACSHA256(macKey))
                expectedMac = hmac.ComputeHash(Combine(PayloadMagic, iv, ciphertext));

            if (!FixedTimeEquals(mac, expectedMac))
                throw new FbmodReaderException("Encrypted fbmod payload failed authentication.");

            using var aes = Aes.Create();
            aes.KeySize = 256;
            aes.BlockSize = 128;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = encKey;
            aes.IV = iv;
            using ICryptoTransform decryptor = aes.CreateDecryptor();
            return decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
        }
        catch (CryptographicException ex)
        {
            throw new FbmodReaderException("Encrypted fbmod payload could not be decrypted.", ex);
        }
        finally
        {
            Clear(encKey);
            Clear(macKey);
        }
    }

    private static byte[] GetEncryptionKey() => DeriveKey(165);

    private static byte[] GetMacKey() => DeriveKey(90);

    private static byte[] DeriveKey(byte discriminator)
    {
        byte[] material = new byte[PartA.Length + PartB.Length];
        try
        {
            for (int i = 0; i < PartA.Length; i++)
                material[i] = (byte)(PartA[i] ^ Mask[i]);
            for (int j = 0; j < PartB.Length; j++)
                material[PartA.Length + j] = (byte)(PartB[j] ^ Mask[j] ^ discriminator);

            using var sha = SHA256.Create();
            return sha.ComputeHash(material);
        }
        finally
        {
            Array.Clear(material, 0, material.Length);
        }
    }

    private static byte[] Combine(params byte[][] arrays)
    {
        int total = 0;
        foreach (byte[] a in arrays)
            total += a?.Length ?? 0;

        byte[] result = new byte[total];
        int offset = 0;
        foreach (byte[] a in arrays)
        {
            if (a is null || a.Length == 0)
                continue;
            Buffer.BlockCopy(a, 0, result, offset, a.Length);
            offset += a.Length;
        }
        return result;
    }

    private static bool FixedTimeEquals(byte[] left, byte[] right)
    {
        if (left.Length != right.Length)
            return false;
        int diff = 0;
        for (int i = 0; i < left.Length; i++)
            diff |= left[i] ^ right[i];
        return diff == 0;
    }

    private static void Clear(byte[]? buffer)
    {
        if (buffer != null)
            Array.Clear(buffer, 0, buffer.Length);
    }
}
