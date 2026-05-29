using Avalonia.Media.Imaging;

#pragma warning disable 300

namespace Github_Trend;

public sealed class GithubContributorPreview
{
    public GithubContributorPreview(string login, string? avatarUrl, Bitmap? avatarImage)
    {
        Login = login;
        AvatarUrl = avatarUrl;
        AvatarImage = avatarImage;
        _ = Login;
        _ = AvatarUrl;
        _ = AvatarImage;
    }

    public string Login { get; }

    public string? AvatarUrl { get; }

    public Bitmap? AvatarImage { get; }
}

#pragma warning restore 300