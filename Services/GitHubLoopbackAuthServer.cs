using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Github_Trend;

public sealed class GitHubLoopbackAuthServer : IDisposable
{
    private readonly Uri _callbackUri;
    private readonly string _expectedPath;
    private readonly HttpListener _listener = new();

    public GitHubLoopbackAuthServer(string callbackUrl)
    {
        if (!Uri.TryCreate(callbackUrl, UriKind.Absolute, out var parsed) || parsed is null)
            throw new ArgumentException("Invalid callback URL.", nameof(callbackUrl));

        _callbackUri = parsed;

        _expectedPath = string.IsNullOrWhiteSpace(_callbackUri.AbsolutePath)
            ? "/"
            : _callbackUri.AbsolutePath.TrimEnd('/');
        var prefix = $"{_callbackUri.Scheme}://{_callbackUri.Host}:{_callbackUri.Port}/";
        _listener.Prefixes.Add(prefix);
    }

    public void Dispose()
    {
        Stop();
        _listener.Close();
    }

    public void Start()
    {
        _listener.Start();
    }

    public async Task<OAuthCallbackResult> WaitForCallbackAsync()
    {
        while (_listener.IsListening)
        {
            var context = await _listener.GetContextAsync();
            if (!IsExpectedPath(context.Request.Url))
            {
                await WriteResponseAsync(context.Response, false,
                    "Cette requête ne correspond pas au callback OAuth attendu.");
                continue;
            }

            var callback = ParseCallback(context.Request.Url);
            await WriteResponseAsync(context.Response, callback.Error is null);
            Stop();
            return callback;
        }

        throw new InvalidOperationException("OAuth callback listener stopped unexpectedly.");
    }

    public void Stop()
    {
        if (_listener.IsListening) _listener.Stop();
    }

    private bool IsExpectedPath(Uri? url)
    {
        if (url is null) return false;

        var requestPath = string.IsNullOrWhiteSpace(url.AbsolutePath) ? "/" : url.AbsolutePath.TrimEnd('/');
        return string.Equals(requestPath, _expectedPath, StringComparison.OrdinalIgnoreCase);
    }

    private static OAuthCallbackResult ParseCallback(Uri? url)
    {
        if (url is null) return new OAuthCallbackResult(null, null, null, "Missing callback URL", null);

        var query = ParseQuery(url.Query);
        query.TryGetValue("code", out var code);
        query.TryGetValue("state", out var state);
        query.TryGetValue("error", out var error);
        query.TryGetValue("error_description", out var errorDescription);
        query.TryGetValue("error_uri", out var errorUri);

        return new OAuthCallbackResult(code, state, error, errorDescription, errorUri);
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(query)) return result;

        var trimmed = query.TrimStart('?');
        foreach (var segment in trimmed.Split('&',
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = segment.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0].Replace('+', ' '));
            var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')) : string.Empty;
            result[key] = value;
        }

        return result;
    }

    private static async Task WriteResponseAsync(HttpListenerResponse response, bool success, string? message = null)
    {
        const string successHtml =
            "<!doctype html><html><head><meta charset=\"utf-8\"><title>GitHub login</title></head><body style=\"font-family:sans-serif;padding:24px;\"><h2>Connexion GitHub terminée</h2><p>Vous pouvez fermer cet onglet et revenir à l’application.</p></body></html>";
        var errorHtml =
            $"<!doctype html><html><head><meta charset=\"utf-8\"><title>GitHub login</title></head><body style=\"font-family:sans-serif;padding:24px;\"><h2>Connexion GitHub interrompue</h2><p>{WebUtility.HtmlEncode(message ?? "Vous pouvez fermer cet onglet et revenir à l’application.")}</p></body></html>";

        var bytes = Encoding.UTF8.GetBytes(success ? successHtml : errorHtml);
        response.StatusCode = success ? (int)HttpStatusCode.OK : (int)HttpStatusCode.BadRequest;
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
        response.OutputStream.Close();
        response.Close();
    }
}

public sealed record OAuthCallbackResult(
    string? Code,
    string? State,
    string? Error,
    string? ErrorDescription,
    string? ErrorUri);