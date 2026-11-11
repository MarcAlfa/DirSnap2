using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using DirSnap2.Services;

namespace DirSnap2.Views;

/// <summary>
/// Single list of the keyboard shortcuts shown to the user (Settings button and F1).
/// Keep it in sync with MainWindow.RegisterKeyboardShortcuts and the KeyDown handlers.
/// </summary>
public static class ShortcutsView
{
    public sealed record Shortcut(string Keys, string Action);

    public static readonly (string Group, Shortcut[] Items)[] Groups =
    {
        ("Navigation", new[]
        {
            new Shortcut("Alt + Left", "Back"),
            new Shortcut("Alt + Right", "Forward"),
            new Shortcut("Alt + Up", "Up to parent folder"),
            new Shortcut("Backspace", "Up to parent folder (file list)"),
            new Shortcut("F5", "Refresh the current folder"),
            new Shortcut("Ctrl + L", "Go to the address bar"),
            new Shortcut("Enter", "Open the typed path (address bar)"),
            new Shortcut("Ctrl + F", "Go to the quick filter"),
        }),
        ("Tabs", new[]
        {
            new Shortcut("Ctrl + T", "New tab"),
            new Shortcut("Ctrl + W", "Close the current tab"),
            new Shortcut("Ctrl + Tab", "Next tab"),
            new Shortcut("Ctrl + Shift + Tab", "Previous tab"),
        }),
        ("File list", new[]
        {
            new Shortcut("Enter", "Open the selected folder or file"),
            new Shortcut("Double-click", "Open folder / open file in edit mode"),
            new Shortcut("Ins", "Add the selected item to the Snap List"),
        }),
        ("Editor", new[]
        {
            new Shortcut("Ctrl + E", "Switch between read-only and edit mode"),
            new Shortcut("Ctrl + S", "Save the file"),
        }),
        ("Snap List", new[]
        {
            new Shortcut("Del", "Remove the selected items"),
        }),
        ("Help", new[]
        {
            new Shortcut("F1", "Show this list"),
        }),
    };

    /// <summary>Builds the shortcut table (used inside a Flyout or a ContentDialog).</summary>
    public static FrameworkElement Build()
    {
        var accent = Application.Current.Resources.TryGetValue("AppAccentBrush", out var b) && b is Brush ab
            ? ab
            : null;

        var stack = new StackPanel { Spacing = 4, Width = 400 };

        foreach (var (group, items) in Groups)
        {
            stack.Children.Add(new TextBlock
            {
                Text = group,
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Opacity = 0.7,
                Margin = new Thickness(0, 8, 0, 2)
            });

            foreach (var shortcut in items)
            {
                var row = new Grid { ColumnSpacing = 12 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var keys = new TextBlock
                {
                    Text = shortcut.Keys,
                    FontSize = 12,
                    FontFamily = new FontFamily("Consolas, Cascadia Code, Courier New"),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                };
                if (accent != null)
                    keys.Foreground = accent;

                var action = new TextBlock
                {
                    Text = shortcut.Action,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(action, 1);

                row.Children.Add(keys);
                row.Children.Add(action);
                stack.Children.Add(row);
            }
        }

        return new ScrollViewer
        {
            Content = stack,
            MaxHeight = 460,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(0, 0, 12, 0),
            RequestedTheme = ThemeService.ActiveTheme
        };
    }

    public static async Task ShowDialogAsync(XamlRoot xamlRoot)
    {
        var dialog = new ContentDialog
        {
            Title = "Keyboard Shortcuts",
            Content = Build(),
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = xamlRoot,
            RequestedTheme = ThemeService.ActiveTheme
        };
        await dialog.ShowAsync();
    }
}
