using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Github_Trend;

public sealed class GitHubTokenProtector
{
    private const string TokenPurpose = "Github_Trend.TokenProtector.v1";
    private readonly byte[] _key;

    public GitHubTokenProtector()
    {
        _key = LoadOrCreateKey();
    }

    public string Protect(string? plaintext)
    {
        if (string.IsNullOrWhiteSpace(plaintext)) return string.Empty;

        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[16];

        using (var aes = new AesGcm(_key, 16))
        {
            aes.Encrypt(nonce, plainBytes, cipherBytes, tag, Encoding.UTF8.GetBytes(TokenPurpose));
        }

        var payload = new byte[nonce.Length + tag.Length + cipherBytes.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, payload, nonce.Length, tag.Length);
        Buffer.BlockCopy(cipherBytes, 0, payload, nonce.Length + tag.Length, cipherBytes.Length);
        return Convert.ToBase64String(payload);
    }

    public string? Unprotect(string? encrypted)
    {
        if (string.IsNullOrWhiteSpace(encrypted)) return null;

        var payload = Convert.FromBase64String(encrypted);
        if (payload.Length < 12 + 16) return null;

        var nonce = payload[..12];
        var tag = payload[12..28];
        var cipherBytes = payload[28..];
        var plainBytes = new byte[cipherBytes.Length];

        try
        {
            using (var aes = new AesGcm(_key, 16))
            {
                aes.Decrypt(nonce, cipherBytes, tag, plainBytes, Encoding.UTF8.GetBytes(TokenPurpose));
            }
        }
        catch (CryptographicException)
        {
            return null;
        }

        return Encoding.UTF8.GetString(plainBytes);
    }

    private static byte[] LoadOrCreateKey()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Github_Trend");
        Directory.CreateDirectory(folder);

        var keyPath = Path.Combine(folder, "github-token.key");
        if (File.Exists(keyPath))
        {
            var existing = Convert.FromBase64String(File.ReadAllText(keyPath));
            if (existing.Length == 32) return existing;
        }

        var key = RandomNumberGenerator.GetBytes(32);
        File.WriteAllText(keyPath, Convert.ToBase64String(key));

        TryRestrictPermissions(keyPath);
        return key;
    }

    private static void TryRestrictPermissions(string keyPath)
    {
        try
        {
#if NET6_0_OR_GREATER
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                File.SetUnixFileMode(keyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
#endif
        }
        catch
        {
            // best-effort only
        }
    }
}