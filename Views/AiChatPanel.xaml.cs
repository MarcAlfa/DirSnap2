using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using Windows.System;
using DirSnap2.Models;
using DirSnap2.Services;
using DirSnap2.ViewModels;

namespace DirSnap2.Views;

/// <summary>Picks the bubble template of a chat message (question, reply, error).</summary>
public sealed partial class AiMessageTemplateSelector : DataTemplateSelector
{
    public DataTemplate? UserTemplate { get; set; }
    public DataTemplate? AssistantTemplate { get; set; }
    public DataTemplate? ErrorTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) => item switch
    {
        AiMessage { IsUser: true } => UserTemplate,
        AiMessage { IsError: true } => ErrorTemplate,
        _ => AssistantTemplate
    };

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) => SelectTemplateCore(item);
}

/// <summary>
/// The AI Assistant chat UI, hosted both by the toolbar flyout and by the detached window.
/// DataContext: <see cref="AiChatViewModel"/>.
/// </summary>
public sealed partial class AiChatPanel : UserControl
{
    private AiChatViewModel? _vm;

    /// <summary>Flyout host: "Detach" was clicked.</summary>
    public event EventHandler? DetachRequested;

    /// <summary>The model badge was clicked: open the AI settings.</summary>
    public event EventHandler? SettingsRequested;

    /// <summary>Window host: the "always on top" pin was toggled.</summary>
    public event EventHandler<bool>? PinChanged;

    /// <summary>The window that owns this panel (needed by the file save picker).</summary>
    public Window? HostWindow { get; set; }

    public AiChatPanel()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => AttachViewModel(DataContext as AiChatViewModel);
        Unloaded += (_, _) => AttachViewModel(null);
        Loaded += (_, _) =>
        {
            AttachViewModel(DataContext as AiChatViewModel);
            ScrollToBottom(force: true);
        };
    }

    /// <summary>Window host: shows the pin toggle instead of "Detach".</summary>
    public void SetDetachedMode(bool detached, bool pinned = true)
    {
        DetachButton.Visibility = detached ? Visibility.Collapsed : Visibility.Visible;
        PinToggle.Visibility = detached ? Visibility.Visible : Visibility.Collapsed;
        PinToggle.IsChecked = pinned;
    }

    public void FocusInput()
    {
        InputBox.Focus(FocusState.Programmatic);
        InputBox.SelectionStart = InputBox.Text?.Length ?? 0;
    }

    private void AttachViewModel(AiChatViewModel? vm)
    {
        if (_vm == vm)
            return;
        if (_vm != null)
            _vm.ConversationUpdated -= Vm_ConversationUpdated;
        _vm = vm;
        if (_vm != null)
            _vm.ConversationUpdated += Vm_ConversationUpdated;
    }

    private void Vm_ConversationUpdated(object? sender, EventArgs e) => ScrollToBottom(force: false);

    /// <summary>Follows the streaming reply, unless the user scrolled up to read something.</summary>
    private void ScrollToBottom(bool force)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                MessagesScroller.UpdateLayout();
                double distance = MessagesScroller.ExtentHeight - MessagesScroller.ViewportHeight - MessagesScroller.VerticalOffset;
                if (force || distance < 120 || _vm?.IsBusy == true && distance < 400)
                    MessagesScroller.ChangeView(null, MessagesScroller.ScrollableHeight, null, disableAnimation: true);
            }
            catch
            {
                // layout not ready
            }
        });
    }

    // ==================== HEADER ====================

    private void ModelBadge_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty);

    private void NewChat_Click(object sender, RoutedEventArgs e)
    {
        _vm?.NewChat();
        FocusInput();
    }

    private void Detach_Click(object sender, RoutedEventArgs e) => DetachRequested?.Invoke(this, EventArgs.Empty);

    private void PinToggle_Click(object sender, RoutedEventArgs e) => PinChanged?.Invoke(this, PinToggle.IsChecked == true);

    private async void ExportChat_Click(object sender, RoutedEventArgs e)
    {
        if (_vm == null || !_vm.HasMessages)
            return;
        try
        {
            var picker = new FileSavePicker();
            if (HostWindow != null)
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(HostWindow));
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeChoices.Add("Markdown (*.md)", new List<string> { ".md" });
            picker.SuggestedFileName = $"AI_Chat_{DateTime.Now:yyyyMMdd_HHmm}";

            var file = await picker.PickSaveFileAsync();
            if (file == null)
                return;

            await System.IO.File.WriteAllTextAsync(file.Path, _vm.BuildConversationMarkdown(), new System.Text.UTF8Encoding(false));
            _vm.StatusText = $"Chat exported to '{file.Name}'";
        }
        catch (Exception ex)
        {
            AppLogger.Error($"[AiChatPanel] Export chat failed: {ex.Message}", ex);
            _vm.StatusText = $"Export failed: {ex.Message}";
        }
    }

    // ==================== CONTEXT ====================

    private void ExtraTabsMenu_Opening(object sender, object e)
    {
        ExtraTabsMenu.Items.Clear();
        if (_vm == null)
            return;

        var tabs = _vm.GetOtherTabs().ToList();
        if (tabs.Count == 0)
        {
            ExtraTabsMenu.Items.Add(new MenuFlyoutItem { Text = "No other tab has a file open", IsEnabled = false });
            return;
        }

        foreach (var (tab, label, selected) in tabs)
        {
            var item = new ToggleMenuFlyoutItem { Text = label, IsChecked = selected };
            var captured = tab;
            item.Click += (s, _) => _vm.SetExtraTab(captured, ((ToggleMenuFlyoutItem)s).IsChecked);
            ExtraTabsMenu.Items.Add(item);
        }
    }

    private void QuickAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string label })
            _vm?.RunQuickAction(label);
    }

    // ==================== INPUT ====================

    private void InputBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
            return;

        var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
        if ((shift & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0)
            return; // Shift+Enter: new line

        e.Handled = true;
        if (_vm is { IsBusy: false })
            _vm.SendOrStop();
    }

    private void Send_Click(object sender, RoutedEventArgs e)
    {
        _vm?.SendOrStop();
        FocusInput();
    }

    // ==================== REPLY ACTIONS ====================

    private async void CopyReply_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AiMessage message } btn)
            return;
        var dp = new DataPackage();
        dp.SetText(message.Content);
        Clipboard.SetContent(dp);

        // Brief "Copied" feedback on the button itself
        if (btn.Content is StackPanel { Children.Count: 2 } sp && sp.Children[1] is TextBlock label)
        {
            label.Text = "Copied";
            await Task.Delay(1200);
            label.Text = "Copy";
        }
    }

    private void SaveReply_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: AiMessage message })
            _vm?.SaveReplyAsMarkdown(message);
    }

    private void SendToEditor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: AiMessage message } target || _vm == null)
            return;

        var menu = new MenuFlyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Bottom };
        var replace = new MenuFlyoutItem { Text = "Replace editor content", Icon = new FontIcon { Glyph = "\uE8AB" } };
        replace.Click += (_, _) => _vm.SendReplyToEditor(message, append: false);
        var append = new MenuFlyoutItem { Text = "Append at the end", Icon = new FontIcon { Glyph = "\uE710" } };
        append.Click += (_, _) => _vm.SendReplyToEditor(message, append: true);
        menu.Items.Add(replace);
        menu.Items.Add(append);
        menu.ShowAt(target);
    }
}
