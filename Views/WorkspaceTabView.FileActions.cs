using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using DirSnap2.Models;
using DirSnap2.Services;

namespace DirSnap2.Views;

/// <summary>
/// File actions of the list: Copy / Cut / Paste / Duplicate (like Explorer), Rename, Delete,
/// New folder and the quick "new .txt / .md" buttons.
/// </summary>
public sealed partial class WorkspaceTabView
{
    /// <summary>The row a context-menu item belongs to (right-click does not always select it).</summary>
    private FSItem? GetContextItem(object sender) =>
        (sender as FrameworkElement)?.DataContext as FSItem ?? ViewModel?.SelectedItem;

    // ==================== COPY ====================

    private void Context_Copy_Click(object sender, RoutedEventArgs e)
    {
        if (GetContextItem(sender) is { } item)
            _ = CopyItemToClipboardAsync(item);
    }

    private void Context_Cut_Click(object sender, RoutedEventArgs e)
    {
        if (GetContextItem(sender) is { } item)
            _ = CopyItemToClipboardAsync(item, cut: true);
    }

    /// <summary>
    /// Puts the file/folder on the clipboard as a real item: Ctrl+V in Explorer pastes a copy
    /// (or moves it, after Cut).
    /// </summary>
    private async Task CopyItemToClipboardAsync(FSItem item, bool cut = false)
    {
        var vm = ViewModel;
        if (vm == null || item.Name == "..") return;

        try
        {
            IStorageItem storageItem = item.IsDirectory
                ? await StorageFolder.GetFolderFromPathAsync(item.Path)
                : await StorageFile.GetFileFromPathAsync(item.Path);

            var dp = new DataPackage { RequestedOperation = cut ? DataPackageOperation.Move : DataPackageOperation.Copy };
            dp.SetStorageItems(new[] { storageItem });
            Clipboard.SetContent(dp);
            Clipboard.Flush(); // stays on the clipboard after DirSnap 2 is closed

            vm.StatusMessage = cut
                ? $"Cut '{item.Name}': paste it in another folder to move it"
                : $"Copied '{item.Name}' to the clipboard";
        }
        catch (Exception ex)
        {
            AppLogger.Error($"[Copy] Error copying {item.Path} to clipboard", ex);
            vm.StatusMessage = $"Cannot copy '{item.Name}': {ex.Message}";
        }
    }

    // ==================== PASTE / DUPLICATE ====================

    private void Context_Paste_Click(object sender, RoutedEventArgs e) => _ = PasteFromClipboardAsync();

    private void Context_Duplicate_Click(object sender, RoutedEventArgs e)
    {
        if (GetContextItem(sender) is { } item)
            _ = DuplicateItemAsync(item);
    }

    /// <summary>Owner window of the Windows copy/move dialogs.</summary>
    private IntPtr GetOwnerWindowHandle()
    {
        try
        {
            if (XamlRoot?.ContentIslandEnvironment is { } env)
                return Microsoft.UI.Win32Interop.GetWindowFromWindowId(env.AppWindowId);
        }
        catch { }
        return IntPtr.Zero;
    }

    private static bool IsSameFolder(string? a, string? b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
        try
        {
            return string.Equals(ShellFileOperations.CleanPath(a), ShellFileOperations.CleanPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Ctrl+V: pastes the files/folders on the clipboard (from DirSnap 2, Explorer, mail...) into the
    /// current folder. After a Cut they are moved. Windows asks when a name already exists.
    /// </summary>
    private async Task PasteFromClipboardAsync()
    {
        var vm = ViewModel;
        if (vm == null || !Directory.Exists(vm.CurrentPath)) return;
        string dest = vm.CurrentPath;

        List<string> sources;
        bool move;
        try
        {
            var content = Clipboard.GetContent();
            if (!content.Contains(StandardDataFormats.StorageItems))
            {
                vm.StatusMessage = "Nothing to paste: the clipboard holds no files or folders";
                return;
            }
            var items = await content.GetStorageItemsAsync();
            sources = items.Select(i => i.Path).Where(p => !string.IsNullOrEmpty(p)).ToList();
            move = content.RequestedOperation.HasFlag(DataPackageOperation.Move);
        }
        catch (Exception ex)
        {
            AppLogger.Error("[Paste] Cannot read the clipboard", ex);
            vm.StatusMessage = $"Cannot read the clipboard: {ex.Message}";
            return;
        }

        if (sources.Count == 0)
        {
            vm.StatusMessage = "Nothing to paste: the clipboard items have no path on disk";
            return;
        }

        if (move && sources.All(s => IsSameFolder(Path.GetDirectoryName(s), dest)))
        {
            vm.StatusMessage = "Already in this folder";
            return;
        }

        string? openFile = vm.HasActiveFile ? vm.CurrentEditingFilePath : null;
        string? movedOpenSource = move ? sources.FirstOrDefault(s => IsSameOrInside(openFile, s)) : null;
        if (movedOpenSource != null && vm.IsEditorDirty)
        {
            await ShowMessageAsync("Paste", $"'{vm.CurrentEditingFileName}' has unsaved changes. Save or discard them before moving it.");
            return;
        }

        // A copy into the folder it comes from gets "name - Copy" instead of the replace question
        bool renameOnCollision = !move && sources.Any(s => IsSameFolder(Path.GetDirectoryName(s), dest));
        var owner = GetOwnerWindowHandle();
        string what = sources.Count == 1 ? $"'{Path.GetFileName(sources[0])}'" : $"{sources.Count} items";

        bool done;
        try
        {
            vm.StatusMessage = move ? $"Moving {what}..." : $"Copying {what}...";
            done = await Task.Run(() => move
                ? ShellFileOperations.Move(sources, dest, owner)
                : ShellFileOperations.Copy(sources, dest, renameOnCollision, owner));
        }
        catch (Exception ex)
        {
            AppLogger.Error($"[Paste] {(move ? "Move" : "Copy")} into {dest}", ex);
            vm.StatusMessage = $"Paste failed: {ex.Message}";
            if (ReferenceEquals(vm, ViewModel)) vm.RefreshCommand.Execute(null);
            return;
        }

        if (!ReferenceEquals(vm, ViewModel)) return;
        AppLogger.Info($"[Paste] {(move ? "Moved" : "Copied")} {sources.Count} item(s) into {dest} (completed: {done})");

        if (move && done)
        {
            // Like Explorer: what was cut is pasted only once
            try { Clipboard.Clear(); } catch { }

            // The open file moved with them: follow it
            if (movedOpenSource != null && openFile != null)
            {
                var newOpen = Path.Combine(dest, Path.GetFileName(movedOpenSource.TrimEnd('\\'))) + openFile.Substring(movedOpenSource.TrimEnd('\\').Length);
                if (File.Exists(newOpen)) vm.SelectFile(newOpen);
                else vm.ClearFileSelection();
            }
        }

        vm.StatusMessage = !done ? "Paste cancelled" : move ? $"Moved {what} here" : $"Pasted {what}";
        vm.RefreshCommand.Execute(null);
    }

    /// <summary>Copy of the file/folder next to it ("name - Copy.ext").</summary>
    private async Task DuplicateItemAsync(FSItem item)
    {
        var vm = ViewModel;
        var parent = Path.GetDirectoryName(item.Path);
        if (vm == null || item.Name == ".." || string.IsNullOrEmpty(parent)) return;

        var owner = GetOwnerWindowHandle();
        try
        {
            bool done = await Task.Run(() => ShellFileOperations.Copy(new[] { item.Path }, parent, renameOnCollision: true, owner));
            if (!ReferenceEquals(vm, ViewModel)) return;
            vm.StatusMessage = done ? $"Duplicated '{item.Name}'" : "Duplicate cancelled";
            AppLogger.Info($"[Duplicate] {item.Path} (completed: {done})");
        }
        catch (Exception ex)
        {
            AppLogger.Error($"[Duplicate] {item.Path}", ex);
            vm.StatusMessage = $"Cannot duplicate '{item.Name}': {ex.Message}";
        }
        vm.RefreshCommand.Execute(null);
    }

    // ==================== NEW FOLDER ====================

    private void NewFolderButton_Click(object sender, RoutedEventArgs e) => _ = CreateFolderAsync();
    private void Context_NewFolder_Click(object sender, RoutedEventArgs e) => _ = CreateFolderAsync();

    private async Task CreateFolderAsync()
    {
        var vm = ViewModel;
        if (vm == null || XamlRoot == null || !Directory.Exists(vm.CurrentPath)) return;
        string parent = vm.CurrentPath;

        string suggested = "New folder";
        for (int n = 2; n < 1000 && (Directory.Exists(Path.Combine(parent, suggested)) || File.Exists(Path.Combine(parent, suggested))); n++)
            suggested = $"New folder ({n})";

        var nameBox = new TextBox { Text = suggested, MinWidth = 380 };
        var errorText = new TextBlock
        {
            Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Visibility = Visibility.Collapsed
        };
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(nameBox);
        panel.Children.Add(errorText);
        nameBox.Loaded += (_, _) =>
        {
            nameBox.Focus(FocusState.Programmatic);
            nameBox.SelectAll();
        };

        var dialog = new ContentDialog
        {
            Title = "New Folder",
            Content = panel,
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.ActiveTheme
        };

        string? created = null;
        dialog.PrimaryButtonClick += (_, args) =>
        {
            var name = nameBox.Text.Trim();
            var error = ValidateName(name);
            var target = error == null ? Path.Combine(parent, name) : null;
            if (error == null && (Directory.Exists(target) || File.Exists(target)))
                error = $"'{name}' already exists in this folder.";

            if (error == null)
            {
                try
                {
                    Directory.CreateDirectory(target!);
                    created = target;
                    return;
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"[NewFolder] {target}", ex);
                    error = $"Cannot create the folder: {ex.Message}";
                }
            }

            errorText.Text = error;
            errorText.Visibility = Visibility.Visible;
            args.Cancel = true;
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary || created == null || !ReferenceEquals(vm, ViewModel))
            return;

        AppLogger.Info($"[NewFolder] Created {created}");
        vm.StatusMessage = $"Created folder '{Path.GetFileName(created)}'";
        vm.RefreshAndSelectFolder(created);
    }

    // ==================== RENAME ====================

    private void Context_Rename_Click(object sender, RoutedEventArgs e)
    {
        if (GetContextItem(sender) is { } item)
            _ = RenameItemAsync(item);
    }

    private static bool IsSameOrInside(string? path, string folder) =>
        !string.IsNullOrEmpty(path) &&
        (string.Equals(path, folder, StringComparison.OrdinalIgnoreCase) ||
         path.StartsWith(folder.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase));

    private async Task RenameItemAsync(FSItem item)
    {
        var vm = ViewModel;
        if (vm == null || item.Name == ".." || XamlRoot == null) return;

        string oldPath = item.Path;
        bool isDir = item.IsDirectory;
        string? openFile = vm.HasActiveFile ? vm.CurrentEditingFilePath : null;
        bool affectsOpenFile = isDir ? IsSameOrInside(openFile, oldPath) : vm.IsCurrentFile(oldPath);

        // The editor holds edits for this file (or for a file inside this folder)
        if (affectsOpenFile && vm.IsEditorDirty)
        {
            await new ContentDialog
            {
                Title = "Rename",
                Content = $"'{vm.CurrentEditingFileName}' has unsaved changes. Save or discard them before renaming.",
                CloseButtonText = "OK",
                XamlRoot = XamlRoot,
                RequestedTheme = ThemeService.ActiveTheme
            }.ShowAsync();
            return;
        }

        var nameBox = new TextBox { Text = item.Name, MinWidth = 380 };
        var errorText = new TextBlock
        {
            Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Visibility = Visibility.Collapsed
        };
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(nameBox);
        panel.Children.Add(errorText);

        // Like Explorer: select the name without the extension
        nameBox.Loaded += (_, _) =>
        {
            nameBox.Focus(FocusState.Programmatic);
            int dot = isDir ? -1 : item.Name.LastIndexOf('.');
            nameBox.Select(0, dot > 0 ? dot : item.Name.Length);
        };

        var dialog = new ContentDialog
        {
            Title = isDir ? "Rename Folder" : "Rename File",
            Content = panel,
            PrimaryButtonText = "Rename",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.ActiveTheme
        };

        string? newPath = null;
        dialog.PrimaryButtonClick += (_, args) =>
        {
            var newName = nameBox.Text.Trim();
            var error = ValidateNewName(newName, item.Name, oldPath, isDir, out var target);
            if (error != null)
            {
                errorText.Text = error;
                errorText.Visibility = Visibility.Visible;
                args.Cancel = true;
                return;
            }

            try
            {
                MoveItem(oldPath, target!, isDir);
                newPath = target;
            }
            catch (Exception ex)
            {
                AppLogger.Error($"[Rename] {oldPath} -> {target}", ex);
                errorText.Text = $"Cannot rename: {ex.Message}";
                errorText.Visibility = Visibility.Visible;
                args.Cancel = true;
            }
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary || newPath == null || !ReferenceEquals(vm, ViewModel))
            return;

        AppLogger.Info($"[Rename] {oldPath} -> {newPath}");
        vm.StatusMessage = $"Renamed '{item.Name}' to '{Path.GetFileName(newPath)}'";

        // The open file (or its folder) moved: reopen it from the new path
        if (affectsOpenFile && openFile != null)
        {
            var reopened = isDir ? newPath + openFile.Substring(oldPath.Length) : newPath;
            vm.SelectFile(reopened);
        }

        if (isDir && vm.SelectedItem is { } sel && string.Equals(sel.Path, oldPath, StringComparison.OrdinalIgnoreCase))
            vm.RefreshAndSelectFolder(newPath);
        else
            vm.RefreshCommand.Execute(null);
    }

    private static string? ValidateNewName(string newName, string oldName, string oldPath, bool isDir, out string? target)
    {
        target = null;
        if (ValidateName(newName) is { } invalid)
            return invalid;
        if (newName == oldName)
            return "The name is the same.";

        var parent = Path.GetDirectoryName(oldPath);
        if (string.IsNullOrEmpty(parent))
            return "Drives cannot be renamed.";

        target = Path.Combine(parent, newName);
        bool caseOnly = string.Equals(newName, oldName, StringComparison.OrdinalIgnoreCase);
        if (!caseOnly && (File.Exists(target) || Directory.Exists(target)))
            return $"'{newName}' already exists in this folder.";

        return null;
    }

    /// <summary>Rules every file/folder name must follow; null when the name is fine.</summary>
    private static string? ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "The name cannot be empty.";
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return "A name cannot contain any of these characters:  \\ / : * ? \" < > |";
        if (name.EndsWith('.'))
            return "A name cannot end with a dot.";
        return null;
    }

    private static void MoveItem(string oldPath, string newPath, bool isDir)
    {
        bool caseOnly = string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase);
        if (!isDir)
        {
            File.Move(oldPath, newPath);
            return;
        }

        if (caseOnly)
        {
            // Directory.Move refuses a rename that only changes upper/lower case: go through a temp name
            var temp = oldPath.TrimEnd('\\') + "_rename_" + Guid.NewGuid().ToString("N")[..8];
            Directory.Move(oldPath, temp);
            Directory.Move(temp, newPath);
        }
        else
        {
            Directory.Move(oldPath, newPath);
        }
    }

    // ==================== DELETE (RECYCLE BIN) ====================

    private void Context_Delete_Click(object sender, RoutedEventArgs e)
    {
        if (GetContextItem(sender) is { } item)
            _ = DeleteItemAsync(item);
    }

    private async Task<bool> ShowConfirmAsync(string title, string message, string primary)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = primary,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close, // Enter alone never deletes
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.ActiveTheme
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        await new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = "OK",
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.ActiveTheme
        }.ShowAsync();
    }

    /// <summary>Moves a file/folder to the Recycle Bin after two confirmations.</summary>
    private async Task DeleteItemAsync(FSItem item)
    {
        var vm = ViewModel;
        if (vm == null || item.Name == ".." || XamlRoot == null) return;

        string path = item.Path;
        bool isDir = item.IsDirectory;
        string? openFile = vm.HasActiveFile ? vm.CurrentEditingFilePath : null;
        bool affectsOpenFile = isDir ? IsSameOrInside(openFile, path) : vm.IsCurrentFile(path);

        try
        {
            if (affectsOpenFile && vm.IsEditorDirty)
            {
                await ShowMessageAsync("Delete", $"'{vm.CurrentEditingFileName}' has unsaved changes. Save or discard them before deleting.");
                return;
            }

            if (RecycleBinService.GetUnsupportedReason(path) is { } reason)
            {
                await ShowMessageAsync("Delete", $"'{item.Name}' was not deleted.\n\n{reason}\n\nDelete it from Explorer if you really want to.");
                return;
            }

            // Confirmation 1: what is going to the Recycle Bin
            string what = isDir ? "folder" : "file";
            string details = string.Empty;
            if (isDir)
            {
                try
                {
                    int dirs = Directory.EnumerateDirectories(path).Count();
                    int files = Directory.EnumerateFiles(path).Count();
                    details = dirs + files == 0
                        ? "\n\nThe folder is empty."
                        : $"\n\nIt contains {dirs} folders and {files} files (plus everything inside the subfolders).";
                }
                catch { }
            }
            if (!await ShowConfirmAsync($"Delete {what}", $"Move the {what} '{item.Name}' to the Recycle Bin?{details}", "Delete"))
                return;

            // Confirmation 2
            if (!await ShowConfirmAsync("Are you sure?",
                    $"'{item.Name}' will be moved to the Recycle Bin.\nYou can restore it from there.", "Yes, move to Recycle Bin"))
                return;

            if (!ReferenceEquals(vm, ViewModel)) return;

            // Close the file before deleting it (the editor does not lock it, but keep things clean)
            if (affectsOpenFile)
                vm.ClearFileSelection();

            RecycleBinService.MoveToRecycleBin(path);
            AppLogger.Info($"[Delete] Moved to Recycle Bin: {path}");
            vm.StatusMessage = $"'{item.Name}' moved to the Recycle Bin";
            vm.RefreshCommand.Execute(null);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"[Delete] {path}", ex);
            vm.StatusMessage = $"Cannot delete '{item.Name}': {ex.Message}";
            vm.RefreshCommand.Execute(null); // a folder may have been emptied only in part
            await ShowMessageAsync("Delete", $"'{item.Name}' was not deleted.\n\n{ex.Message}");
        }
    }

    // ==================== NEW .TXT / .MD ====================

    private void NewTxtButton_Click(object sender, RoutedEventArgs e) => _ = CreateQuickFileAsync(".txt");
    private void NewMdButton_Click(object sender, RoutedEventArgs e) => _ = CreateQuickFileAsync(".md");

    /// <summary>Creates an empty file named after the current date/time and opens it in edit mode.</summary>
    private async Task CreateQuickFileAsync(string extension)
    {
        var vm = ViewModel;
        if (vm == null || !Directory.Exists(vm.CurrentPath)) return;

        // The new file replaces the one in the editor
        if (vm.IsEditorDirty)
        {
            bool proceed = await vm.ConfirmDiscardUnsavedChangesAsync(XamlRoot);
            if (!proceed || !ReferenceEquals(vm, ViewModel)) return;
        }

        string baseName = DateTime.Now.ToString("yyyy-MM-dd_HH-mm");
        string path = Path.Combine(vm.CurrentPath, baseName + extension);
        try
        {
            for (int n = 2; ; n++)
            {
                try
                {
                    // CreateNew never overwrites: an existing name moves on to _2, _3...
                    using (new FileStream(path, FileMode.CreateNew, FileAccess.Write)) { }
                    break;
                }
                catch (IOException) when (File.Exists(path) || Directory.Exists(path))
                {
                    if (n > 999) throw;
                    path = Path.Combine(vm.CurrentPath, $"{baseName}_{n}{extension}");
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error($"[NewFile] Cannot create {path}", ex);
            vm.StatusMessage = $"Cannot create the file: {ex.Message}";
            return;
        }

        AppLogger.Info($"[NewFile] Created {path}");
        vm.SelectFile(path);
        vm.SelectedItem = null; // the list rebuild then selects the open (new) file
        vm.RefreshCommand.Execute(null);

        if (vm.IsEditAllowed)
        {
            vm.UnlockEditor();
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (EditorTextBox.Visibility == Visibility.Visible)
                    EditorTextBox.Focus(FocusState.Programmatic);
            });
        }
        vm.StatusMessage = $"Created '{Path.GetFileName(path)}'";
    }
}
