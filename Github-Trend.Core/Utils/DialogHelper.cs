using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Serilog;

namespace Github_Trend.Utils;

public static class DialogHelper
{
    private static async Task<bool> ShowConfirmCoreAsync(
        Window owner,
        string title,
        string? message,
        string? subtitle,
        string confirmText,
        string cancelText,
        IBrush? confirmBackground,
        bool accentStyle = false)
    {
        var loc = Localization.Localization.Instance;
        var confirm = new Window
        {
            Title = title,
            Width = 380,
            Height = subtitle is not null ? 200 : 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = owner.FindResource("BackgroundPrimaryBrush") as IBrush,
        };

        var panel = new StackPanel { Spacing = 16, Margin = new(20) };

        if (message is not null)
        {
            panel.Children.Add(new TextBlock
            {
                Text = message,
                FontSize = 16,
                FontWeight = FontWeight.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Foreground = owner.FindResource("TextPrimaryBrush") as IBrush,
            });
        }

        if (subtitle is not null)
        {
            panel.Children.Add(new TextBlock
            {
                Text = subtitle,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = owner.FindResource("TextSecondaryBrush") as IBrush,
            });
        }

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };

        var cancelBtn = new Button
        {
            Content = cancelText,
            Padding = new(16, 8),
            Background = owner.FindResource("BackgroundTertiaryBrush") as IBrush,
            Foreground = owner.FindResource("TextPrimaryBrush") as IBrush,
            CornerRadius = new(8),
        };

        var confirmBtn = new Button
        {
            Content = confirmText,
            Padding = new(16, 8),
            CornerRadius = new(8),
        };

        if (accentStyle)
        {
            confirmBtn.Background = owner.FindResource("AccentPrimaryBrush") as IBrush;
            confirmBtn.Foreground = owner.FindResource("TextOnAccentBrush") as IBrush;
        }
        else
        {
            confirmBtn.Background = confirmBackground ?? (owner.FindResource("ErrorBrush") as IBrush);
            confirmBtn.Foreground = Brushes.White;
        }

        var tcs = new TaskCompletionSource<bool>();
        cancelBtn.Click += (_, _) => { Log.Debug("ConfirmDialog: cancelled"); tcs.TrySetResult(false); confirm.Close(); };
        confirmBtn.Click += (_, _) => { Log.Debug("ConfirmDialog: confirmed ({Text})", confirmText); tcs.TrySetResult(true); confirm.Close(); };

        buttonRow.Children.Add(cancelBtn);
        buttonRow.Children.Add(confirmBtn);
        panel.Children.Add(buttonRow);
        confirm.Content = panel;

        await confirm.ShowDialog(owner);
        return await tcs.Task;
    }

    public static Task<bool> ShowConfirmAsync(
        Window owner,
        string title,
        string message,
        string? confirmText = null,
        IBrush? confirmBackground = null)
    {
        var loc = Localization.Localization.Instance;
        return ShowConfirmCoreAsync(
            owner,
            title,
            message,
            subtitle: null,
            confirmText: confirmText ?? loc.GetString("Delete"),
            cancelText: loc.GetString("Cancel"),
            confirmBackground: confirmBackground,
            accentStyle: false);
    }

    public static Task<bool> ShowConfirmActionAsync(
        Window owner,
        string title,
        string actionText)
    {
        var loc = Localization.Localization.Instance;
        return ShowConfirmCoreAsync(
            owner,
            title,
            message: title,
            subtitle: null,
            confirmText: actionText,
            cancelText: loc.GetString("Cancel"),
            confirmBackground: null,
            accentStyle: true);
    }

    public static Task<bool> ShowConfirmSignOutAsync(Window owner)
    {
        var loc = Localization.Localization.Instance;
        return ShowConfirmCoreAsync(
            owner,
            title: loc.GetString("GitHubSignOutButtonText"),
            message: loc.GetString("ConfirmSignOut"),
            subtitle: loc.GetString("ConfirmSignOutMessage"),
            confirmText: loc.GetString("GitHubSignOutButtonText"),
            cancelText: loc.GetString("Cancel"),
            confirmBackground: null,
            accentStyle: false);
    }
}
