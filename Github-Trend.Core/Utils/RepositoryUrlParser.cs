using System;

namespace Github_Trend.Utils;

public static class RepositoryUrlParser
{
    public static bool TryParse(string? url, out string owner, out string name)
    {
        owner = string.Empty;
        name = string.Empty;

        if (string.IsNullOrWhiteSpace(url))
            return false;

        var candidate = url.Trim();

        if (candidate.Contains("://", StringComparison.OrdinalIgnoreCase))
        {
            if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
            {
                var parts = uri.AbsolutePath.Split(
                    '/',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                );
                if (parts.Length >= 2)
                {
                    owner = parts[^2];
                    name = parts[^1];
                    return true;
                }
            }
            return false;
        }

        if (candidate.Contains('/'))
        {
            var parts = candidate.Split(
                '/',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            );
            if (parts.Length >= 2)
            {
                owner = parts[^2];
                name = parts[^1];
                return true;
            }
        }

        return false;
    }

    public static string GetSlug(string? url)
    {
        if (TryParse(url, out var owner, out var name))
            return $"{owner}/{name}";

        return url?.Trim() ?? string.Empty;
    }

    public static string GetOwner(string? url)
    {
        TryParse(url, out var owner, out _);
        return owner;
    }

    public static string GetName(string? url)
    {
        TryParse(url, out _, out var name);
        return name;
    }
}
