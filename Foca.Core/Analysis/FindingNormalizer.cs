using System.Globalization;
using System.Text;
using Foca.Core.Model;

namespace Foca.Core.Analysis;

/// <summary>
/// Canonical keys used to group the same leaked value across documents. The original WinForms code
/// compared raw strings, so "ACME\jdoe", "jdoe" and "JDoe" appeared as three different users.
/// </summary>
public static class FindingNormalizer
{
    public static string Normalize(FindingKind kind, string value)
    {
        string v = CollapseWhitespace(value).Trim();
        switch (kind)
        {
            case FindingKind.User:
                return NormalizeUser(v);
            case FindingKind.Email:
                return v.ToLowerInvariant();
            case FindingKind.Server:
                return v.TrimEnd('.').ToLowerInvariant();
            case FindingKind.Path:
                return v.Replace('/', '\\').TrimEnd('\\').ToLowerInvariant();
            default:
                return RemoveDiacritics(v).ToLowerInvariant();
        }
    }

    /// <summary>
    /// "ACME\jdoe" -> "jdoe", "jdoe@acme.com" -> "jdoe", "José Pérez" -> "jose perez".
    /// The account part identifies the person; the domain is reported separately.
    /// </summary>
    public static string NormalizeUser(string user)
    {
        string v = user.Trim().Trim('"', '\'');
        int slash = v.LastIndexOf('\\');
        if (slash >= 0 && slash < v.Length - 1)
            v = v.Substring(slash + 1);
        int at = v.IndexOf('@');
        if (at > 0)
            v = v.Substring(0, at);
        return RemoveDiacritics(v).ToLowerInvariant();
    }

    /// <summary>Returns the Windows domain or e-mail domain of an account name, if any.</summary>
    public static string? AccountDomain(string user)
    {
        string v = user.Trim();
        int slash = v.IndexOf('\\');
        if (slash > 0)
            return v.Substring(0, slash).ToLowerInvariant();
        int at = v.IndexOf('@');
        if (at > 0 && at < v.Length - 1)
            return v.Substring(at + 1).ToLowerInvariant();
        return null;
    }

    public static string RemoveDiacritics(string value)
    {
        string decomposed = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (char c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        // Vietnamese đ/Đ is a separate letter, not d plus a combining mark
        return sb.ToString().Normalize(NormalizationForm.FormC).Replace('đ', 'd').Replace('Đ', 'D');
    }

    private static string CollapseWhitespace(string value)
    {
        var sb = new StringBuilder(value.Length);
        bool lastWasSpace = false;
        foreach (char c in value)
        {
            if (char.IsWhiteSpace(c) || char.IsControl(c))
            {
                if (!lastWasSpace)
                    sb.Append(' ');
                lastWasSpace = true;
            }
            else
            {
                sb.Append(c);
                lastWasSpace = false;
            }
        }
        return sb.ToString();
    }
}
