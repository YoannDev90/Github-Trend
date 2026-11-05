using System.Collections.Generic;

namespace Github_Trend;

public sealed record GithubColorsCatalog(Dictionary<string, GithubColorEntry> Colors);