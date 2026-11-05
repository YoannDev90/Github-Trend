using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;

#pragma warning disable 300

namespace Github_Trend;

public sealed class GithubContributorPreview : IDisposable, INotifyPropertyChanged
{
    private Bitmap? _avatarImage;

    public GithubContributorPreview(string login, string? avatarUrl, Bitmap? avatarImage)
    {
        Login = login;
        AvatarUrl = avatarUrl;
        _avatarImage = avatarImage;
    }

    public string Login { get; }

    public string? AvatarUrl { get; }

    public Bitmap? AvatarImage
    {
        get => _avatarImage;
        set
        {
            if (ReferenceEquals(_avatarImage, value)) return;
            var old = _avatarImage;
            _avatarImage = value;
            OnPropertyChanged();
            old?.Dispose();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public void Dispose()
    {
        _avatarImage?.Dispose();
    }
}

#pragma warning restore 300