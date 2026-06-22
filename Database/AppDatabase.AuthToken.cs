using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace Github_Trend.Database;

partial class AppDatabase
{
    public async Task<JsonElement?> GetCurrentAuthTokenAsync()
    {
        await EnsureInitializedAsync();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText =
            @"SELECT user_id, github_account_id, access_token_encrypted, refresh_token_encrypted,
                     expires_at, refresh_token_expires_at, scope_list_json, created_at, updated_at,
                     revoked_at, login, name, email, avatar_url
              FROM auth_tokens
              WHERE revoked_at IS NULL
              ORDER BY updated_at DESC LIMIT 1";
        using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        var obj = new Dictionary<string, object?>
        {
            ["user_id"] = reader.GetString(0),
            ["github_account_id"] = reader.GetInt64(1),
            ["access_token_encrypted"] = reader.GetString(2),
            ["refresh_token_encrypted"] = reader.IsDBNull(3) ? null : reader.GetString(3),
            ["expires_at"] = reader.GetString(4),
            ["refresh_token_expires_at"] = reader.IsDBNull(5) ? null : reader.GetString(5),
            ["scope_list_json"] = reader.GetString(6),
            ["created_at"] = reader.GetString(7),
            ["updated_at"] = reader.GetString(8),
            ["revoked_at"] = reader.IsDBNull(9) ? null : reader.GetString(9),
            ["login"] = reader.IsDBNull(10) ? null : reader.GetString(10),
            ["name"] = reader.IsDBNull(11) ? null : reader.GetString(11),
            ["email"] = reader.IsDBNull(12) ? null : reader.GetString(12),
            ["avatar_url"] = reader.IsDBNull(13) ? null : reader.GetString(13),
        };
        return JsonSerializer.SerializeToElement(obj);
    }

    public async Task UpsertAuthTokenAsync(
        string userId,
        long githubAccountId,
        string accessTokenEncrypted,
        string? refreshTokenEncrypted,
        DateTimeOffset expiresAt,
        DateTimeOffset? refreshTokenExpiresAt,
        IReadOnlyList<string> scopeList,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        DateTimeOffset? revokedAt,
        string? login,
        string? name,
        string? email,
        string? avatarUrl
    )
    {
        await EnsureInitializedAsync();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText =
            @"INSERT OR REPLACE INTO auth_tokens
              (user_id, github_account_id, access_token_encrypted, refresh_token_encrypted,
               expires_at, refresh_token_expires_at, scope_list_json, created_at, updated_at,
               revoked_at, login, name, email, avatar_url)
              VALUES ($uid, $ghid, $at, $rt, $exp, $rtexp, $scopes, $cat, $uat, $rev, $login, $name, $email, $avatar)";
        cmd.Parameters.AddWithValue("$uid", userId);
        cmd.Parameters.AddWithValue("$ghid", githubAccountId);
        cmd.Parameters.AddWithValue("$at", accessTokenEncrypted);
        cmd.Parameters.AddWithValue("$rt", refreshTokenEncrypted ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$exp", expiresAt.ToString("o"));
        cmd.Parameters.AddWithValue(
            "$rtexp",
            refreshTokenExpiresAt?.ToString("o") ?? (object)DBNull.Value
        );
        cmd.Parameters.AddWithValue("$scopes", JsonSerializer.Serialize(scopeList));
        cmd.Parameters.AddWithValue("$cat", createdAt.ToString("o"));
        cmd.Parameters.AddWithValue("$uat", updatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("$rev", revokedAt?.ToString("o") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$login", login ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$name", name ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$email", email ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$avatar", avatarUrl ?? (object)DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }
}
