using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using Serilog;

namespace Github_Trend.Services.GraphQL;

public static class GraphQlQueryLoader
{
    private static readonly ConcurrentDictionary<string, string> Cache = new();
    private static readonly string QueriesDirectory;

    static GraphQlQueryLoader()
    {
        var baseDir = AppContext.BaseDirectory;
        QueriesDirectory = Path.Combine(baseDir, "Services", "GraphQL");

        if (!Directory.Exists(QueriesDirectory))
        {
            var altPath = Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? baseDir,
                "Services",
                "GraphQL"
            );
            if (Directory.Exists(altPath))
                QueriesDirectory = altPath;
        }
    }

    public static string Load(string queryFileName)
    {
        return Cache.GetOrAdd(queryFileName, name =>
        {
            var path = Path.Combine(QueriesDirectory, name);
            if (!File.Exists(path))
                throw new FileNotFoundException(
                    $"GraphQL query file not found: {path}",
                    path
                );

            var content = File.ReadAllText(path);
            Log.Debug("Loaded GraphQL query: {FileName} ({Length} chars)", name, content.Length);
            return content;
        });
    }

    public static string ExtractMutation(string fileContent, string mutationName)
    {
        return ExtractOperation(fileContent, "mutation", mutationName);
    }

    public static string ExtractQuery(string fileContent, string queryName)
    {
        return ExtractOperation(fileContent, "query", queryName);
    }

    private static string ExtractOperation(string fileContent, string kind, string operationName)
    {
        var start = fileContent.IndexOf($"{kind} {operationName}", StringComparison.Ordinal);
        if (start < 0)
            throw new InvalidOperationException(
                $"{kind} '{operationName}' not found in query file"
            );

        var depth = 0;
        var foundFirstBrace = false;
        var end = start;
        var inString = false;

        for (var i = start; i < fileContent.Length; i++)
        {
            var c = fileContent[i];

            if (c == '"' && (i == 0 || fileContent[i - 1] != '\\'))
                inString = !inString;

            if (inString)
                continue;

            if (c == '#')
            {
                while (i < fileContent.Length && fileContent[i] != '\n')
                    i++;
                continue;
            }

            if (c == '{')
            {
                depth++;
                foundFirstBrace = true;
            }
            else if (c == '}')
            {
                depth--;
                if (foundFirstBrace && depth == 0)
                {
                    end = i + 1;
                    break;
                }
            }
        }

        return fileContent[start..end];
    }
}
