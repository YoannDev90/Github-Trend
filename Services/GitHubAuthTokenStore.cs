using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Github_Trend;

public sealed class GitHubAuthTokenStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _filePath;

    public GitHubAuthTokenStore()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Github_Trend");
        Directory.CreateDirectory(folder);
        _filePath = Path.Combine(folder, "github-auth-tokens.json");
    }

    public async Task<IReadOnlyList<GitHubAuthTokenRecord>> LoadAllAsync()
    {
        if (!File.Exists(_filePath)) return Array.Empty<GitHubAuthTokenRecord>();

        var json = await File.ReadAllTextAsync(_filePath);
        return JsonSerializer.Deserialize<List<GitHubAuthTokenRecord>>(json, JsonOptions) ??
               new List<GitHubAuthTokenRecord>();
    }

    public async Task<GitHubAuthTokenRecord?> GetCurrentAsync()
    {
        var records = await LoadAllAsync();
        return records
            .Where(record => record.RevokedAt is null)
            .OrderByDescending(record => record.UpdatedAt)
            .FirstOrDefault();
    }

    public async Task UpsertAsync(GitHubAuthTokenRecord record)
    {
        var records = (await LoadAllAsync()).ToList();
        records.RemoveAll(existing =>
            existing.UserId == record.UserId && existing.GitHubAccountId == record.GitHubAccountId);
        records.Add(record);
        await WriteAllAsync(records);
    }

    public async Task MarkRevokedAsync(long githubAccountId)
    {
        var records = (await LoadAllAsync()).ToList();
        var target = records.FirstOrDefault(record => record.GitHubAccountId == githubAccountId);
        if (target is null) return;

        target.RevokedAt = DateTimeOffset.UtcNow;
        target.UpdatedAt = DateTimeOffset.UtcNow;
        await WriteAllAsync(records);
    }

    private async Task WriteAllAsync(IReadOnlyCollection<GitHubAuthTokenRecord> records)
    {
        var json = JsonSerializer.Serialize(records, JsonOptions);
        await File.WriteAllTextAsync(_filePath, json);
    }
}