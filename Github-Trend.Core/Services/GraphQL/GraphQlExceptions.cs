using System;

namespace Github_Trend.Services.GraphQL;

public sealed class GraphQlRateLimitException : Exception
{
    public GraphQlRateLimitException()
        : base("GitHub GraphQL rate limit reached") { }
}

public sealed class GraphQlAuthException : Exception
{
    public GraphQlAuthException(string message)
        : base(message) { }
}
