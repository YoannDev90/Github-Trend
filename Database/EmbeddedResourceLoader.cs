using System.IO;
using System.Reflection;

namespace Github_Trend.Database;

internal static class EmbeddedResourceLoader
{
    public static string Load(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var fullName = $"{assembly.GetName().Name}.{resourceName}";
        using var stream = assembly.GetManifestResourceStream(fullName);
        if (stream is null)
            throw new FileNotFoundException($"Embedded resource not found: {fullName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
