using System.Diagnostics;
using System.IO;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DirSnap2.Models;
using DirSnap2.Services;

namespace DirSnap2.Views;

/// <summary>
/// "Folder info" window (right-click on a folder): size, files and subfolders at every level,
/// computed in the background with live totals.
/// </summary>
public sealed partial class WorkspaceTabView
{
    private void Context_FolderInfo_Click(object sender, RoutedEventArgs e)
    {
        if (GetContextItem(sender) is { IsDirectory: true } item && item.Name != "..")
            _ = ShowFolderInfoAsync(item.Path);
    }

    private async Task ShowFolderInfoAsync(string path)
    {
        if (XamlRoot == null || !Directory.Exists(path)) return;

        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 6, MinWidth = 380 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Secondary text by opacity: a brush read from the app resources would not follow the dialog theme
        const double secondary = 0.7;
        int row = 0;
        TextBlock AddRow(string label, string value = "...")
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var l = new TextBlock { Text = label, Opacity = secondary, FontSize = 13 };
            var v = new TextBlock { Text = value, FontSize = 13, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
            Grid.SetRow(l, row);
            Grid.SetRow(v, row);
            Grid.SetColumn(v, 1);
            grid.Children.Add(l);
            grid.Children.Add(v);
            row++;
            return v;
        }

        var sizeText = AddRow("Size");
        var filesText = AddRow("Files");
        var foldersText = AddRow("Subfolders");
        var directText = AddRow("Directly inside");
        string modified = "-";
        try { modified = Directory.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm"); } catch { }
        AddRow("Last modified", modified);
        var newestText = AddRow("Newest file");
        ToolTipService.SetToolTip(newestText, "Most recently modified file, at any level. The folder date above changes only when items directly inside it are added, removed or renamed (like Explorer).");

        var pathText = new TextBlock
        {
            Text = path,
            FontSize = 11,
            Opacity = secondary,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            Margin = new Thickness(0, 0, 0, 4)
        };
        var statusText = new TextBlock { Text = "Calculating...", FontSize = 11, Opacity = secondary, TextWrapping = TextWrapping.Wrap };
        var progress = new ProgressBar { IsIndeterminate = true };

        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(pathText);
        panel.Children.Add(grid);
        panel.Children.Add(progress);
        panel.Children.Add(statusText);

        void Show(FolderStatsService.Stats s)
        {
            sizeText.Text = s.TotalBytes >= 1024 * 1024
                ? $"{s.TotalBytes / (1024.0 * 1024):N1} MB   ({s.TotalBytes:N0} bytes)"
                : $"{FSItem.FormatSize(s.TotalBytes)}   ({s.TotalBytes:N0} bytes)";
            filesText.Text = $"{s.Files:N0}";
            foldersText.Text = $"{s.Folders:N0}";
            directText.Text = $"{s.DirectFolders:N0} folders, {s.DirectFiles:N0} files";
            if (s.NewestFile == null)
            {
                newestText.Text = "-";
            }
            else
            {
                // Path relative to the folder, so the subfolder is visible too
                var root = path.TrimEnd('\\');
                var rel = s.NewestFile.Length > root.Length
                    ? s.NewestFile.Substring(root.Length).TrimStart('\\')
                    : Path.GetFileName(s.NewestFile);
                newestText.Text = $"{s.NewestFileTime:yyyy-MM-dd HH:mm}   {rel}";
            }
        }

        var dialog = new ContentDialog
        {
            Title = Path.GetFileName(path.TrimEnd('\\')) is { Length: > 0 } name ? name : path,
            Content = panel,
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.ActiveTheme
        };

        using var cts = new CancellationTokenSource();
        var dq = DispatcherQueue;
        var watch = Stopwatch.StartNew();
        var compute = Task.Run(() => FolderStatsService.Compute(path,
            partial => dq.TryEnqueue(() =>
            {
                if (cts.IsCancellationRequested) return;
                Show(partial);
                statusText.Text = $"Calculating...   {partial.Files:N0} files so far";
            }),
            cts.Token));

        _ = compute.ContinueWith(t => dq.TryEnqueue(() =>
        {
            if (cts.IsCancellationRequested) return;
            progress.Visibility = Visibility.Collapsed;
            if (t.IsFaulted)
            {
                statusText.Text = $"Cannot calculate: {t.Exception?.GetBaseException().Message}";
                AppLogger.Error($"[FolderInfo] {path}", t.Exception?.GetBaseException());
                return;
            }
            var s = t.Result;
            Show(s);
            string text = $"Calculated in {watch.Elapsed.TotalSeconds:N1} s, all levels included.";
            if (s.Unreadable > 0)
                text += $"   {s.Unreadable:N0} folders could not be read (no access): the totals leave them out.";
            statusText.Text = text;
        }), TaskScheduler.Default);

        await dialog.ShowAsync();
        cts.Cancel(); // closing the window stops a calculation still running
    }
}
