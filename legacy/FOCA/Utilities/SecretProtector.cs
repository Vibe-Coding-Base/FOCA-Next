using System;
using System.Security.Cryptography;
using System.Text;

namespace FOCA.Utilites
{
    /// <summary>
    /// Protects API keys stored in the FOCA database with DPAPI (current Windows user scope).
    /// Values written before encryption existed have no prefix and are returned unchanged,
    /// so existing databases keep working and get encrypted on the next save.
    /// </summary>
    public static class SecretProtector
    {
        private const string Prefix = "dpapi:";
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("FOCA.ApiKeys.v1");

        public static string Protect(string plainText)
        {
            if (String.IsNullOrEmpty(plainText) || plainText.StartsWith(Prefix, StringComparison.Ordinal))
                return plainText;

            byte[] cipher = ProtectedData.Protect(Encoding.UTF8.GetBytes(plainText), Entropy, DataProtectionScope.CurrentUser);
            return Prefix + Convert.ToBase64String(cipher);
        }

        /// <returns>The plain value, or an empty string when it was protected by another Windows user or machine.</returns>
        public static string Unprotect(string storedValue)
        {
            if (String.IsNullOrEmpty(storedValue) || !storedValue.StartsWith(Prefix, StringComparison.Ordinal))
                return storedValue;

            try
            {
                byte[] plain = ProtectedData.Unprotect(Convert.FromBase64String(storedValue.Substring(Prefix.Length)), Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
            catch (Exception e) when (e is CryptographicException || e is FormatException)
            {
                return String.Empty;
            }
        }
    }
}
