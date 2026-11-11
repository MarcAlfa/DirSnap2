using System.Diagnostics;
using System.IO;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using DirSnap2.Models;
using DirSnap2.Services;
using DirSnap2.ViewModels;

namespace DirSnap2.Views;

public sealed partial class WorkspaceTabView : UserControl
{
    private WorkspaceTabViewModel? _currentVm;

    public WorkspaceTabViewModel? ViewModel
    {
        get => DataContext as WorkspaceTabViewModel;
        set => DataContext = value;
    }

    // Splitter drag state
    private bool _isDraggingSplitter;
    private double _splitterStartX;
    private double _splitterStartWidth;

    private TreeViewNode? _currentTargetTreeNode;

    public event Action<string>? OpenInNewTabRequested;

    public WorkspaceTabView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            UpdateEditorViewMode();
        };

        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (_currentVm != null)
        {
            _currentVm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _currentVm = ViewModel;

        if (_currentVm != null)
        {
            _currentVm.PropertyChanged += OnViewModelPropertyChanged;
            _currentVm.SyncSelectedFile();
            UpdateEditorViewMode();
            if (_currentVm.SelectedItem != null)
            {
                FlatListView.ScrollIntoView(_currentVm.SelectedItem);
            }
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkspaceTabViewModel.IsPreviewUnlocked)
            or nameof(WorkspaceTabViewModel.IsEditorLocked)
            or nameof(WorkspaceTabViewModel.IsMarkdownFile)
            or nameof(WorkspaceTabViewModel.EditorText)
            or nameof(WorkspaceTabViewModel.HasActiveFile)
            or nameof(WorkspaceTabViewModel.EditorWrap)
            or nameof(WorkspaceTabViewModel.EditorTextWrapping))
        {
            UpdateEditorViewMode();
        }
    }

    public void UpdateEditorViewMode()
    {
        if (ViewModel == null || !ViewModel.IsPreviewUnlocked)
        {
            MarkdownScrollViewer.Visibility = Visibility.Collapsed;
            EditorTextBox.Visibility = Visibility.Collapsed;
            return;
        }

        var scrollMode = ViewModel.EditorWrap ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        MarkdownScrollViewer.HorizontalScrollBarVisibility = scrollMode;
        ScrollViewer.SetHorizontalScrollBarVisibility(EditorTextBox, scrollMode);
        MarkdownRichTextBlock.TextWrapping = ViewModel.EditorTextWrapping;
        EditorTextBox.TextWrapping = ViewModel.EditorTextWrapping;

        if (ViewModel.IsMarkdownFile && ViewModel.IsEditorLocked)
        {
            bool isDark = ViewModel.ConfigService?.Current?.Theme != "light";
            MarkdownHighlighter.Highlight(MarkdownRichTextBlock, ViewModel.EditorText, isDark);
            MarkdownScrollViewer.Visibility = Visibility.Visible;
            EditorTextBox.Visibility = Visibility.Collapsed;
        }
        else
        {
            MarkdownScrollViewer.Visibility = Visibility.Collapsed;
            EditorTextBox.Visibility = Visibility.Visible;
        }
    }

    // ==================== CURRENT FOLDER TREE FLYOUT ====================

    private void CurrentFolderTreeButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            SyncTreeToCurrentPath(ViewModel.CurrentPath);
        }
    }

    private void CloseTreeFlyout_Click(object sender, RoutedEventArgs e)
    {
        FolderTreeFlyout.Hide();
    }

    private void FolderTreeFlyout_Opened(object sender, object e)
    {
        void ScrollToTarget()
        {
            if (_currentTargetTreeNode is { } targetNode)
            {
                var curr = targetNode.Parent;
                while (curr != null)
                {
                    curr.IsExpanded = true;
                    HierarchicalTreeView.Expand(curr);
                    curr = curr.Parent;
                }

                targetNode.IsExpanded = true;
                HierarchicalTreeView.Expand(targetNode);
                HierarchicalTreeView.SelectedNode = targetNode;

                if (HierarchicalTreeView.ContainerFromNode(targetNode) is TreeViewItem tvi)
                {
                    tvi.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
                    tvi.Focus(FocusState.Programmatic);
                }
            }
        }

        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, ScrollToTarget);
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, ScrollToTarget);
    }

    // ==================== SPLITTER DRAG ====================

    private void Splitter_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
    }

    private void Splitter_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDraggingSplitter)
        {
            ProtectedCursor = null;
        }
    }

    private void Splitter_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _isDraggingSplitter = true;
        _splitterStartX = e.GetCurrentPoint(this).Position.X;
        _splitterStartWidth = LeftColumn.ActualWidth;
        SplitterBorder.CapturePointer(e.Pointer);
    }

    private void Splitter_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDraggingSplitter) return;

        double currentX = e.GetCurrentPoint(this).Position.X;
        double delta = currentX - _splitterStartX;
        double newWidth = Math.Max(260, Math.Min(ActualWidth - 300, _splitterStartWidth + delta));
        LeftColumn.Width = new GridLength(newWidth);
    }

    private void Splitter_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_isDraggingSplitter)
        {
            _isDraggingSplitter = false;
            SplitterBorder.ReleasePointerCapture(e.Pointer);
            ProtectedCursor = null;
        }
    }

    // ==================== FLAT LIST ACTIONS ====================

    private void FlatListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel != null && FlatListView.SelectedItem is FSItem item)
        {
            if (item.Name != ".." && !item.IsDirectory)
            {
                ViewModel.SelectFile(item.Path);
            }
            else
            {
                ViewModel.ClearFileSelection();
            }
        }
    }

    private void FlatListView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (ViewModel?.SelectedItem is not { } item) return;

        if (item.Name == "..")
        {
            ViewModel.GoUpCommand.Execute(null);
        }
        else if (item.IsDirectory)
        {
            ViewModel.NavigateToCommand.Execute(item.Path);
        }
        else
        {
            ViewModel.SelectFile(item.Path);
            if (ViewModel.CanUnlockToEdit)
            {
                ViewModel.UnlockEditorCommand.Execute(null);
            }
        }
    }

    private void FlatListView_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (ViewModel?.SelectedItem is not { } item) return;

        if (e.Key == VirtualKey.Insert)
        {
            ViewModel.AddItemToSnapListCommand.Execute(item);
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Enter)
        {
            if (item.Name == "..")
                ViewModel.GoUpCommand.Execute(null);
            else if (item.IsDirectory)
                ViewModel.NavigateToCommand.Execute(item.Path);
            else
                ViewModel.SelectFile(item.Path);

            e.Handled = true;
        }
    }

    private void SnapItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: FSItem item } && ViewModel != null)
        {
            ViewModel.AddItemToSnapListCommand.Execute(item);
        }
    }

    // Context Menu Handlers
    private void Context_AddToSnapList_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.SelectedItem is { } item)
            ViewModel.AddItemToSnapListCommand.Execute(item);
    }

    private void Context_OpenInNewTab_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.SelectedItem is { IsDirectory: true } item)
            OpenInNewTabRequested?.Invoke(item.Path);
    }

    private void Context_OpenExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.SelectedItem is { } item)
            ViewModel.OpenInExplorerCommand.Execute(item.Path);
    }

    private void Context_OpenTerminal_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
            ViewModel.OpenInTerminalCommand.Execute(null);
    }

    private void Context_CopyFullPath_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.SelectedItem is { } item)
        {
            var dp = new DataPackage();
            dp.SetText(item.Path);
            Clipboard.SetContent(dp);
        }
    }

    private void Context_CopyWslPath_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.SelectedItem is { } item)
        {
            var full = System.IO.Path.GetFullPath(item.Path);
            string wsl = full;
            if (full.Length >= 2 && full[1] == ':')
            {
                var drive = char.ToLowerInvariant(full[0]);
                var rest = full.Substring(2).Replace('\\', '/');
                wsl = $"/mnt/{drive}{rest}";
            }
            var dp = new DataPackage();
            dp.SetText(wsl);
            Clipboard.SetContent(dp);
        }
    }

    // ==================== TREE VIEW ACTIONS & SYNC ====================

    public void SyncTreeToCurrentPath(string targetPath)
    {
        if (string.IsNullOrEmpty(targetPath) || !Directory.Exists(targetPath) || ViewModel == null)
            return;

        HierarchicalTreeView.RootNodes.Clear();
        _currentTargetTreeNode = null;

        try
        {
            var rootDir = Path.GetPathRoot(targetPath);
            if (string.IsNullOrEmpty(rootDir))
                rootDir = targetPath;

            var allowedExt = ViewModel.ShowAll ? null : ViewModel.ConfigService.Current.EditorExtensions;

            bool isRootTarget = string.Equals(rootDir.TrimEnd('\\', '/'), targetPath.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

            var rootFsItem = new FSItem
            {
                Name = rootDir,
                Path = rootDir,
                IsDirectory = true,
                IsCurrentFolder = isRootTarget
            };

            var rootNode = new TreeViewNode
            {
                Content = rootFsItem,
                IsExpanded = true,
                HasUnrealizedChildren = false
            };

            var nodesToExpand = new List<TreeViewNode> { rootNode };

            // Build hierarchy segments
            var segments = BuildPathSegments(rootDir, targetPath);

            TreeViewNode currentNode = rootNode;
            string currentPathAccum = rootDir;

            for (int i = 0; i < segments.Count; i++)
            {
                var targetSubPath = segments[i];
                var (dirs, files, _) = ViewModel.FileSystemService.ScanDirectory(currentPathAccum, ViewModel.ShowAll, allowedExt);

                currentNode.Children.Clear();
                TreeViewNode? nextNode = null;

                foreach (var d in dirs)
                {
                    bool isTargetSubPath = string.Equals(d.Path, targetSubPath, StringComparison.OrdinalIgnoreCase);
                    bool isFinalTarget = string.Equals(d.Path.TrimEnd('\\', '/'), targetPath.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
                    d.IsCurrentFolder = isFinalTarget;

                    var dNode = new TreeViewNode
                    {
                        Content = d,
                        HasUnrealizedChildren = !isTargetSubPath,
                        IsExpanded = isTargetSubPath
                    };
                    currentNode.Children.Add(dNode);

                    if (isTargetSubPath)
                    {
                        nextNode = dNode;
                        nodesToExpand.Add(dNode);
                    }
                }

                if (nextNode != null)
                {
                    currentNode = nextNode;
                    currentPathAccum = targetSubPath;
                }
                else
                {
                    break;
                }
            }

            // Populate current folder (leaf)
            if (string.Equals(currentPathAccum.TrimEnd('\\', '/'), targetPath.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
            {
                var (targetDirs, targetFiles, _) = ViewModel.FileSystemService.ScanDirectory(targetPath, ViewModel.ShowAll, allowedExt);
                currentNode.Children.Clear();
                foreach (var d in targetDirs)
                {
                    currentNode.Children.Add(new TreeViewNode
                    {
                        Content = d,
                        HasUnrealizedChildren = true
                    });
                }
                foreach (var f in targetFiles)
                {
                    currentNode.Children.Add(new TreeViewNode
                    {
                        Content = f,
                        HasUnrealizedChildren = false
                    });
                }
                currentNode.IsExpanded = true;
                currentNode.HasUnrealizedChildren = false;
                nodesToExpand.Add(currentNode);
            }

            if (currentNode.Content is FSItem activeItem)
            {
                activeItem.IsCurrentFolder = true;
            }

            _currentTargetTreeNode = currentNode;

            HierarchicalTreeView.RootNodes.Add(rootNode);

            // Explicitly call Expand on all nodes down the chain
            foreach (var n in nodesToExpand)
            {
                n.IsExpanded = true;
                HierarchicalTreeView.Expand(n);
            }

            HierarchicalTreeView.SelectedNode = _currentTargetTreeNode;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SyncTreeToCurrentPath] Error: {ex.Message}");
        }
    }

    private static List<string> BuildPathSegments(string rootDir, string targetPath)
    {
        var result = new List<string>();
        var normRoot = rootDir.TrimEnd('\\', '/');
        var normTarget = targetPath.TrimEnd('\\', '/');

        if (string.Equals(normRoot, normTarget, StringComparison.OrdinalIgnoreCase))
            return result;

        string relative = normTarget.Substring(normRoot.Length).TrimStart('\\', '/');
        var parts = relative.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);

        string current = normRoot.EndsWith(':') ? normRoot + "\\" : normRoot;
        foreach (var p in parts)
        {
            current = Path.Combine(current, p);
            result.Add(current);
        }

        return result;
    }

    private void HierarchicalTreeView_Expanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        var node = args.Node;
        if (node.HasUnrealizedChildren && node.Content is FSItem item && item.IsDirectory && ViewModel != null)
        {
            try
            {
                var allowedExt = ViewModel.ShowAll ? null : ViewModel.ConfigService.Current.EditorExtensions;
                var (dirs, files, _) = ViewModel.FileSystemService.ScanDirectory(item.Path, ViewModel.ShowAll, allowedExt);
                node.Children.Clear();
                foreach (var d in dirs)
                {
                    node.Children.Add(new TreeViewNode { Content = d, HasUnrealizedChildren = true });
                }
                foreach (var f in files)
                {
                    node.Children.Add(new TreeViewNode { Content = f, HasUnrealizedChildren = false });
                }
                node.HasUnrealizedChildren = false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HierarchicalTreeView_Expanding] Error: {ex.Message}");
            }
        }
    }

    private void HierarchicalTreeView_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        var item = (args.InvokedItem as TreeViewNode)?.Content as FSItem ?? args.InvokedItem as FSItem;
        if (item == null || ViewModel == null) return;

        if (item.IsDirectory)
        {
            ViewModel.NavigateToInternal(item.Path, recordHistory: true);
        }
        else
        {
            ViewModel.SelectFile(item.Path);
            FolderTreeFlyout.Hide();
        }
    }

    private void HierarchicalTreeView_SelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem is TreeViewNode { Content: FSItem item } && ViewModel != null)
        {
            if (!item.IsDirectory)
            {
                ViewModel.SelectFile(item.Path);
            }
        }
    }

    private void SnapTreeNode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: FSItem item } && ViewModel != null)
        {
            ViewModel.AddItemToSnapListCommand.Execute(item);
        }
        else if (sender is Button { Tag: TreeViewNode node } && node.Content is FSItem nodeItem && ViewModel != null)
        {
            ViewModel.AddItemToSnapListCommand.Execute(nodeItem);
        }
    }

    private void Context_TreeAddToSnapList_Click(object sender, RoutedEventArgs e)
    {
        if (HierarchicalTreeView.SelectedItem is TreeViewNode { Content: FSItem item } && ViewModel != null)
        {
            ViewModel.AddItemToSnapListCommand.Execute(item);
        }
    }

    private void Context_TreeOpenExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (HierarchicalTreeView.SelectedItem is TreeViewNode { Content: FSItem item } && ViewModel != null)
        {
            ViewModel.OpenInExplorerCommand.Execute(item.Path);
        }
    }

    private void Context_TreeCopyFullPath_Click(object sender, RoutedEventArgs e)
    {
        if (HierarchicalTreeView.SelectedItem is TreeViewNode { Content: FSItem item })
        {
            var dp = new DataPackage();
            dp.SetText(item.Path);
            Clipboard.SetContent(dp);
        }
    }

    // ==================== EDITOR SHORTCUTS & DIRTY ====================

    private void EditorTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
        if ((ctrl & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0 && e.Key == VirtualKey.S)
        {
            ViewModel?.SaveEditorFileCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void EditorTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (ViewModel != null && !ViewModel.IsEditorDirty && !string.IsNullOrEmpty(ViewModel.CurrentEditingFilePath))
        {
            ViewModel.IsEditorDirty = true;
        }
    }
}
