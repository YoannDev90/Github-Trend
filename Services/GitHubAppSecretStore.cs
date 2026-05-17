using System;
using System.IO;
using System.Text.Json;

namespace Github_Trend;

public sealed class GitHubAppSecretStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _filePath;
    private readonly GitHubTokenProtector _protector = new();

    public GitHubAppSecretStore()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Github_Trend");
        Directory.CreateDirectory(folder);
        _filePath = Path.Combine(folder, "github-app-secret.json");
    }

    public string? Load()
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var record = JsonSerializer.Deserialize<SecretRecord>(json, JsonOptions);
            return _protector.Unprotect(record?.EncryptedClientSecret);
        }
        catch
        {
            return null;
        }
    }

    public void Save(string clientSecret)
    {
        if (string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new ArgumentException("Client secret cannot be empty.", nameof(clientSecret));
        }

        var record = new SecretRecord(_protector.Protect(clientSecret), DateTimeOffset.UtcNow);
        var json = JsonSerializer.Serialize(record, JsonOptions);
        File.WriteAllText(_filePath, json);
    }

    private sealed record SecretRecord(string EncryptedClientSecret, DateTimeOffset SavedAt);
}

