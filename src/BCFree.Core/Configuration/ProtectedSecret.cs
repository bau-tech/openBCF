using System.Security.Cryptography;
using System.Text;

namespace BCFree.Core.Configuration;

internal static class ProtectedSecret
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("openBCF.settings.v1");

    public static string? Protect(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return null;

        var bytes = Encoding.UTF8.GetBytes(value);
        var protectedBytes = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    public static string? Unprotect(string? protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue))
            return null;

        try
        {
            var protectedBytes = Convert.FromBase64String(protectedValue);
            var bytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return null;
        }
    }
}
