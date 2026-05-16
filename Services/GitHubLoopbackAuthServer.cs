using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace Github_Trend;

public sealed class GitHubLoopbackAuthServer
{
    private readonly GitHubAuthOptions _options;
    private readonly GitHubAuthenticationService _authService;
    private readonly SemaphoreSlim _startLock = new(1, 1);
    private IHost? _host;

    public GitHubLoopbackAuthServer(GitHubAuthOptions options, GitHubAuthenticationService authService)
    {
        _options = options;
        _authService = authService;
    }

    public async Task StartAsync()
    {
        if (_host is not null)
        {
            return;
        }

        await _startLock.WaitAsync();
        try
        {
            if (_host is not null)
            {
                return;
            }

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls(_options.LocalBaseUrl);
            builder.WebHost.UseSetting(WebHostDefaults.DetailedErrorsKey, "true");
            var app = builder.Build();

            app.MapGet("/auth/github/start", async (HttpContext context) =>
            {
                var flowId = context.Request.Query["flowId"].ToString();
                var url = await _authService.HandleStartAsync(flowId);
                return Results.Redirect(url);
            });

            app.MapGet("/callback", async (HttpContext context) => await HandleCallbackAsync(context));
            app.MapGet("/auth/github/callback", async (HttpContext context) => await HandleCallbackAsync(context));

            app.MapPost("/auth/github/refresh", async () =>
            {
                var result = await _authService.HandleRefreshRequestAsync();
                return result.Success ? Results.Ok(new { message = result.Message }) : Results.BadRequest(new { message = result.Message });
            });

            app.MapGet("/", () => Results.Text("Github Trend auth loopback server is running."));

            _host = app;
            _ = _host.RunAsync();
        }
        finally
        {
            _startLock.Release();
        }
    }

    public async Task StopAsync()
    {
        if (_host is null)
        {
            return;
        }

        await _host.StopAsync();
        _host.Dispose();
        _host = null;
    }

    private async Task<IResult> HandleCallbackAsync(HttpContext context)
    {
        var error = context.Request.Query["error"].ToString();
        if (string.Equals(error, "access_denied", StringComparison.OrdinalIgnoreCase))
        {
            var flowId = context.Request.Query["flowId"].ToString();
            await _authService.RejectPendingFlowAsync(flowId, "Utilisateur a refusé l'autorisation GitHub.");
            return Results.Content(FriendlyPage("Connexion GitHub annulée", "Vous avez refusé l'accès. Vous pouvez relancer la connexion depuis l'application."), "text/html; charset=utf-8");
        }

        var state = context.Request.Query["state"].ToString();
        var code = context.Request.Query["code"].ToString();
        if (string.IsNullOrWhiteSpace(state) || string.IsNullOrWhiteSpace(code))
        {
            return Results.Content(FriendlyPage("Requête invalide", "Le callback GitHub est incomplet."), "text/html; charset=utf-8");
        }

        try
        {
            var session = await _authService.CompleteAuthorizationAsync(state, code);
            var html = FriendlyPage(
                "Connexion réussie",
                $"Connecté en tant que <strong>{WebUtility.HtmlEncode(session.Summary)}</strong>. Tu peux fermer cet onglet et revenir à l'application.");
            return Results.Content(html, "text/html; charset=utf-8");
        }
        catch (Exception ex)
        {
            return Results.Content(FriendlyPage("Connexion GitHub échouée", WebUtility.HtmlEncode(ex.Message)), "text/html; charset=utf-8");
        }
    }

    private static string FriendlyPage(string title, string body)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!doctype html>");
        sb.AppendLine("<html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.AppendLine($"<title>{WebUtility.HtmlEncode(title)}</title>");
        sb.AppendLine("<style>body{font-family:system-ui,-apple-system,Segoe UI,sans-serif;background:#0b1220;color:#e5e7eb;padding:32px;} .card{max-width:640px;margin:0 auto;background:#111827;border:1px solid #334155;border-radius:18px;padding:24px;} h1{margin-top:0;} a{color:#60a5fa;}</style>");
        sb.AppendLine("</head><body><div class=\"card\">");
        sb.AppendLine($"<h1>{WebUtility.HtmlEncode(title)}</h1>");
        sb.AppendLine($"<p>{body}</p>");
        sb.AppendLine("</div></body></html>");
        return sb.ToString();
    }
}


