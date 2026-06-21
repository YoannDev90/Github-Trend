using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Github_Trend.Services.GraphQL;
using Serilog;

namespace Github_Trend;

public static class SaveToStarListDialog
{
    public static async Task<List<string>?> ShowAsync(
        Window owner,
        List<StarListNode> existingLists,
        Func<string, Task<StarListNode?>> createListAsync
    )
    {
        var confirm = new Window
        {
            Title = "Add to star list",
            Width = 420,
            Height = 220,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = owner.FindResource("BackgroundPrimaryBrush") as IBrush,
        };

        var panel = new StackPanel { Spacing = 12, Margin = new(20) };

        panel.Children.Add(new TextBlock
        {
            Text = "Select a star list to add this repository to:",
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            Foreground = owner.FindResource("TextPrimaryBrush") as IBrush,
        });

        var comboBox = new ComboBox
        {
            MinWidth = 300,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            PlaceholderText = "Choose a list...",
        };

        var items = new List<ComboBoxItem>();
        foreach (var list in existingLists)
        {
            items.Add(new ComboBoxItem
            {
                Content = $"{list.Name} ({list.Items?.TotalCount ?? 0} items)",
                Tag = list.Id,
                Foreground = owner.FindResource("TextPrimaryBrush") as IBrush,
                Background = owner.FindResource("BackgroundPrimaryBrush") as IBrush,
            });
        }
        items.Add(new ComboBoxItem
        {
            Content = "── Create new list ──",
            Tag = "__new__",
            Foreground = owner.FindResource("AccentPrimaryBrush") as IBrush,
            Background = owner.FindResource("BackgroundPrimaryBrush") as IBrush,
        });
        foreach (var item in items)
            comboBox.Items.Add(item);
        comboBox.SelectedIndex = 0;

        panel.Children.Add(comboBox);

        var newListPanel = new StackPanel
        {
            Spacing = 8,
            IsVisible = false,
            Orientation = Orientation.Vertical,
        };

        var newListNameBox = new TextBox
        {
            PlaceholderText = "New list name...",
            Foreground = owner.FindResource("TextPrimaryBrush") as IBrush,
            Background = owner.FindResource("BackgroundSecondaryBrush") as IBrush,
        };
        newListPanel.Children.Add(newListNameBox);

        panel.Children.Add(newListPanel);

        comboBox.SelectionChanged += (_, _) =>
        {
            if (comboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                newListPanel.IsVisible = tag == "__new__";
                if (tag == "__new__")
                {
                    confirm.Height = 280;
                    newListNameBox.Focus();
                }
                else
                {
                    confirm.Height = 220;
                }
            }
        };

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new(0, 8, 0, 0),
        };

        var cancelBtn = new Button
        {
            Content = "Cancel",
            Padding = new(16, 8),
            Background = owner.FindResource("BackgroundTertiaryBrush") as IBrush,
            Foreground = owner.FindResource("TextPrimaryBrush") as IBrush,
            BorderThickness = new(0),
            CornerRadius = new(8),
        };
        var okBtn = new Button
        {
            Content = "Add to list",
            Padding = new(16, 8),
            Background = owner.FindResource("AccentPrimaryBrush") as IBrush,
            Foreground = owner.FindResource("TextOnAccentBrush") as IBrush,
            BorderThickness = new(0),
            CornerRadius = new(8),
        };

        var tcs = new TaskCompletionSource<List<string>?>();

        cancelBtn.Click += (_, _) =>
        {
            Log.Debug("SaveToStarList: cancelled");
            tcs.TrySetResult(null);
            confirm.Close();
        };

        okBtn.Click += async (_, _) =>
        {
            if (comboBox.SelectedItem is not ComboBoxItem item || item.Tag is not string tag)
            {
                Log.Debug("SaveToStarList: no selection");
                return;
            }

            if (tag == "__new__")
            {
                var name = newListNameBox.Text?.Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    Log.Debug("SaveToStarList: new list name is empty");
                    return;
                }

                Log.Debug("SaveToStarList: creating new list '{Name}'", name);
                var newList = await createListAsync(name);
                if (newList is not null)
                {
                    tcs.TrySetResult(new List<string> { newList.Id });
                    confirm.Close();
                }
                else
                {
                    Log.Warning("SaveToStarList: failed to create list");
                }
            }
            else
            {
                Log.Debug("SaveToStarList: selected list id={ListId}", tag);
                tcs.TrySetResult(new List<string> { tag });
                confirm.Close();
            }
        };

        buttonRow.Children.Add(cancelBtn);
        buttonRow.Children.Add(okBtn);
        panel.Children.Add(buttonRow);
        confirm.Content = panel;

        await confirm.ShowDialog(owner);
        return await tcs.Task;
    }
}
