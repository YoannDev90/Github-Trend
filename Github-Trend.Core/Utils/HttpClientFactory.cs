using System;
using System.Net.Http;
using Github_Trend.Services;

namespace Github_Trend.Utils;

public static class HttpClientFactory
{
    public static HttpClient Create()
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(AppConfig.HttpClient.PooledConnectionLifetimeMinutes),
            ConnectTimeout = TimeSpan.FromSeconds(AppConfig.HttpClient.ConnectTimeoutSeconds),
            MaxConnectionsPerServer = 10,
        };
        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(AppConfig.HttpClient.RequestTimeoutSeconds),
        };
    }
}
