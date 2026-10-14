using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Foca.Core;

/// <summary>
/// Protects stored secrets (API keys) at rest. On Windows it uses DPAPI (current user); on Linux and
/// macOS there is no OS-managed per-user key available without extra dependencies, so values are
/// obfuscated with a key derived from the machine and user and the file is expected to be in the
/// user's private data folder. This keeps keys out of plain sight; it is not a substitute for disk
/// encryption. Values written in plaintext (e.g. imported) are returned unchanged and encrypted on save.
/// </summary>
public static class SecretProtector
{
    private const string Prefix = "enc:";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("FOCA.Next.Secrets.v1");

    public static string Protect(string? plainText)
    {
        if (string.IsNullOrEmpty(plainText) || plainText.StartsWith(Prefix, StringComparison.Ordinal))
            return plainText ?? string.Empty;

        byte[] cipher = Encrypt(Encoding.UTF8.GetBytes(plainText));
        return Prefix + Convert.ToBase64String(cipher);
    }

    /// <returns>The plain value, or an empty string when it cannot be decrypted on this machine/account.</returns>
    public static string Unprotect(string? storedValue)
    {
        if (string.IsNullOrEmpty(storedValue) || !storedValue.StartsWith(Prefix, StringComparison.Ordinal))
            return storedValue ?? string.Empty;

        try
        {
            byte[] plain = Decrypt(Convert.FromBase64String(storedValue.Substring(Prefix.Length)));
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception e) when (e is CryptographicException || e is FormatException)
        {
            return string.Empty;
        }
    }

    private static byte[] Encrypt(byte[] data)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);

        using Aes aes = Aes.Create();
        aes.Key = DeriveKey();
        aes.GenerateIV();
        using ICryptoTransform encryptor = aes.CreateEncryptor();
        byte[] cipher = encryptor.TransformFinalBlock(data, 0, data.Length);
        byte[] result = new byte[aes.IV.Length + cipher.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(cipher, 0, result, aes.IV.Length, cipher.Length);
        return result;
    }

    private static byte[] Decrypt(byte[] data)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);

        using Aes aes = Aes.Create();
        aes.Key = DeriveKey();
        byte[] iv = new byte[16];
        Buffer.BlockCopy(data, 0, iv, 0, iv.Length);
        aes.IV = iv;
        using ICryptoTransform decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(data, iv.Length, data.Length - iv.Length);
    }

    // Stable per-user/per-machine key material. Not secret-grade, but ties the blob to this environment.
    private static byte[] DeriveKey()
    {
        string material = Environment.UserName + "\n" + Environment.MachineName + "\n" +
                          Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(material), Entropy, 100_000, HashAlgorithmName.SHA256, 32);
    }
}
