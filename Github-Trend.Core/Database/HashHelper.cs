using System;
using System.Security.Cryptography;
using System.Text;

namespace Github_Trend.Database;

internal static class HashHelper
{
    public static string Sha256Hex(string input)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
