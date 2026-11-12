using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using DirSnap2.Models;
using DirSnap2.Services;

namespace DirSnap2.ViewModels;

/// <summary>
/// State of the AI Assistant: conversation, context choices and the running request.
/// Shared by the toolbar flyout and the detached window. The conversation lives only in memory.
/// </summary>
public sealed partial class AiChatViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly IConfigService _configService;
    private readonly ITextFileService _textFileService;
    private readonly IAiService _aiService;

    private readonly HashSet<WorkspaceTabViewModel> _selectedTabs = new();
    private readonly List<WorkspaceTabViewModel> _watchedTabs = new();
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _estimateCts;

    /// <summary>Context text already sent in this conversation: an identical context is not sent again.</summary>
    private string? _lastSentContext;

    public ObservableCollection<AiMessage> Messages { get; } = new();

    [ObservableProperty]
    private string _inputText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SendButtonText))]
    [NotifyPropertyChangedFor(nameof(SendButtonGlyph))]
    private bool _isBusy;

    public string SendButtonText => IsBusy ? "Stop" : "Send";
    public string SendButtonGlyph => IsBusy ? "" : "";

    [ObservableProperty]
    private bool _includeSnapList;

    [ObservableProperty]
    private bool _includeFolderList;

    /// <summary>Tabs with an open file, each with its "send" checkbox.</summary>
    public ObservableCollection<AiTabContextItem> TabItems { get; } = new();

    public bool HasTabItems => TabItems.Count > 0;

    [ObservableProperty]
    private string _snapListLabel = "Snap List (0)";

    [ObservableProperty]
    private string _providerBadgeText = string.Empty;

    [ObservableProperty]
    private string _modelBadgeText = string.Empty;

    [ObservableProperty]
    private string _modelBadgeToolTip = string.Empty;

    [ObservableProperty]
    private string _contextInfoText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string _statusText = string.Empty;

    public bool HasStatus => !string.IsNullOrEmpty(StatusText);

    public bool HasMessages => Messages.Count > 0;

    /// <summary>Raised when the conversation grows or a reply streams, so the views can scroll down.</summary>
    public event EventHandler? ConversationUpdated;

    public AiChatViewModel(
        MainViewModel main,
        IConfigService configService,
        ITextFileService textFileService,
        IAiService aiService)
    {
        _main = main;
        _configService = configService;
        _textFileService = textFileService;
        _aiService = aiService;

        Messages.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasMessages));

        TabItems.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasTabItems));

        _main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.ActiveTab))
            {
                // A new current tab: only its file is ticked, the others are a click away
                _selectedTabs.Clear();
                if (_main.ActiveTab != null)
                    _selectedTabs.Add(_main.ActiveTab);
                RefreshContextLabels();
            }
        };
        _main.Tabs.CollectionChanged += (_, _) =>
        {
            WatchTabs();
            RefreshContextLabels();
        };
        _main.SnapList.ItemsChanged += (_, _) => RefreshContextLabels();

        if (_main.ActiveTab != null)
            _selectedTabs.Add(_main.ActiveTab);
        WatchTabs();
        RefreshSettings();
        RefreshContextLabels();
    }

    // ==================== LABELS & SETTINGS ====================

    /// <summary>Re-reads provider and model from the config (after Settings closes).</summary>
    public void RefreshSettings()
    {
        var s = AiKeyStore.ResolveSettings(_configService.Current);
        var provider = AiProviders.Get(s.Provider);
        ProviderBadgeText = AiProviders.ShortName(provider.Id);
        ModelBadgeText = s.Model;
        bool missingKey = provider.RequiresApiKey && string.IsNullOrWhiteSpace(s.ApiKey);
        ModelBadgeToolTip = missingKey
            ? $"{provider.DisplayName} · {s.Model}\nNo API key: click to open the AI settings"
            : $"{provider.DisplayName} · {s.Model}\nClick to change provider or model";
        if (missingKey && Messages.Count == 0)
            StatusText = $"Add your {provider.DisplayName} API key in Settings → AI Assistant (click the model badge).";
        else if (StatusText.StartsWith("Add your", StringComparison.Ordinal))
            StatusText = string.Empty;
    }

    /// <summary>One entry of the header's provider menu.</summary>
    public sealed record ProviderOption(string Id, string Label, bool IsCurrent);

    /// <summary>All providers, with the model each one would use and a note when its key is missing.</summary>
    public IReadOnlyList<ProviderOption> GetProviderOptions()
    {
        var config = _configService.Current;
        var current = AiProviders.Get(config.AiProvider).Id;
        return AiProviders.All.Select(p =>
        {
            string model = config.AiModels.TryGetValue(p.Id, out var m) && !string.IsNullOrWhiteSpace(m)
                ? m.Trim()
                : p.DefaultModel;
            string label = $"{p.DisplayName}  ·  {model}";
            if (p.RequiresApiKey && !AiKeyStore.HasKey(config, p.Id))
                label += "  (no API key)";
            return new ProviderOption(p.Id, label, p.Id == current);
        }).ToList();
    }

    /// <summary>
    /// Switches provider from the header without opening Settings. The conversation is kept:
    /// the whole history goes to the new provider with the next message.
    /// </summary>
    public void SetProvider(string providerId)
    {
        var provider = AiProviders.Get(providerId);
        if (string.Equals(_configService.Current.AiProvider, provider.Id, StringComparison.OrdinalIgnoreCase))
            return;
        _configService.Current.AiProvider = provider.Id;
        _configService.Save();
        AppLogger.Info($"[AiChat] Provider switched to {provider.Id}");
        RefreshSettings();
        if (!StatusText.StartsWith("Add your", StringComparison.Ordinal))
        {
            bool missingKey = provider.RequiresApiKey && !AiKeyStore.HasKey(_configService.Current, provider.Id);
            StatusText = missingKey
                ? $"No API key for {provider.DisplayName}: add it in Settings → AI Assistant (click the model badge)."
                : HasMessages ? $"Switched to {provider.DisplayName}: the next message continues this chat there." : string.Empty;
        }
    }

    /// <summary>Follows every tab, so the list updates when a file is opened or the folder changes.</summary>
    private void WatchTabs()
    {
        foreach (var t in _watchedTabs)
            t.PropertyChanged -= OnTabPropertyChanged;
        _watchedTabs.Clear();
        foreach (var t in _main.Tabs)
        {
            t.PropertyChanged += OnTabPropertyChanged;
            _watchedTabs.Add(t);
        }
    }

    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkspaceTabViewModel.CurrentEditingFilePath)
            or nameof(WorkspaceTabViewModel.HasActiveFile)
            or nameof(WorkspaceTabViewModel.CurrentPath))
        {
            RefreshContextLabels();
        }
    }

    partial void OnIncludeSnapListChanged(bool value) => ScheduleContextEstimate();
    partial void OnIncludeFolderListChanged(bool value) => ScheduleContextEstimate();

    /// <summary>"Tab 2 - MARCALFA": tab number and the name of its folder.</summary>
    private string TabName(WorkspaceTabViewModel tab) =>
        string.IsNullOrEmpty(tab.CurrentFolderName)
            ? $"Tab {_main.Tabs.IndexOf(tab) + 1}"
            : $"Tab {_main.Tabs.IndexOf(tab) + 1} - {tab.CurrentFolderName}";

    private static bool TabHasFile(WorkspaceTabViewModel tab) =>
        tab.HasActiveFile && !string.IsNullOrEmpty(tab.CurrentEditingFilePath);

    /// <summary>Rebuilds the tab list (max 10 rows) and the Snap List label.</summary>
    public void RefreshContextLabels()
    {
        _selectedTabs.RemoveWhere(t => !_main.Tabs.Contains(t));

        foreach (var item in TabItems)
            item.PropertyChanged -= OnTabItemPropertyChanged;
        TabItems.Clear();

        foreach (var tab in _main.Tabs.Where(TabHasFile))
        {
            var item = new AiTabContextItem(
                tab,
                TabName(tab),
                Path.GetFileName(tab.CurrentEditingFilePath) ?? string.Empty,
                tab == _main.ActiveTab,
                _selectedTabs.Contains(tab));
            item.PropertyChanged += OnTabItemPropertyChanged;
            TabItems.Add(item);
        }

        int files = _main.SnapList.Items.Count(i => !i.IsDirectory);
        SnapListLabel = $"Snap List ({files})";

        ScheduleContextEstimate();
    }

    private void OnTabItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not AiTabContextItem item || e.PropertyName != nameof(AiTabContextItem.IsSelected))
            return;
        if (item.IsSelected)
            _selectedTabs.Add(item.Tab);
        else
            _selectedTabs.Remove(item.Tab);
        ScheduleContextEstimate();
    }

    // ==================== CONTEXT ====================

    /// <summary>Collects the chosen sources. Must run on the UI thread (reads the editors' text).</summary>
    private (List<AiContextFile> Files, string? FolderPath) GatherSources()
    {
        var files = new List<AiContextFile>();
        var active = _main.ActiveTab;

        // Current tab first, then the others in tab order
        foreach (var tab in _main.Tabs.OrderBy(t => t == active ? 0 : 1))
        {
            if (!_selectedTabs.Contains(tab) || !TabHasFile(tab))
                continue;
            string source = tab == active ? $"{TabName(tab)} (Active)" : TabName(tab);
            files.Add(new AiContextFile(source, tab.CurrentEditingFilePath!, tab.IsPreviewUnlocked ? tab.EditorText : null));
        }

        if (IncludeSnapList)
        {
            foreach (var item in _main.SnapList.Items.Where(i => !i.IsDirectory))
                files.Add(new AiContextFile("Snap List", item.Path, null));
        }

        string? folder = IncludeFolderList ? active?.CurrentPath : null;
        return (files, folder);
    }

    private async Task<AiContextResult> BuildContextAsync(CancellationToken ct = default)
    {
        var (files, folder) = GatherSources();
        if (files.Count == 0 && folder == null)
            return new AiContextResult();
        return await Task.Run(() => AiContextBuilder.Build(files, folder, _textFileService), ct);
    }

    private void ScheduleContextEstimate() => _ = UpdateContextEstimateAsync();

    /// <summary>Shows the size of the selected context next to the input (debounced).</summary>
    private async Task UpdateContextEstimateAsync()
    {
        _estimateCts?.Cancel();
        var cts = new CancellationTokenSource();
        _estimateCts = cts;
        try
        {
            await Task.Delay(250, cts.Token);
            var ctx = await BuildContextAsync(cts.Token);
            if (cts.IsCancellationRequested)
                return;

            int max = MaxContextChars;
            if (ctx.IsEmpty)
                ContextInfoText = "No context: only your question is sent";
            else if (ctx.CharCount > max)
                ContextInfoText = $"⚠ Context too large: {ctx.CharCount:N0} chars (limit {max:N0})";
            else
                ContextInfoText = ctx.Text == _lastSentContext
                    ? $"{ctx.Summary} (already sent)"
                    : ctx.Summary;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"[AiChat] Context estimate failed: {ex.Message}");
        }
    }

    private int MaxContextChars =>
        _configService.Current.AiMaxContextChars > 0 ? _configService.Current.AiMaxContextChars : 120000;

    // ==================== SEND / STOP ====================

    /// <summary>Send button: sends the typed question, or stops the running reply.</summary>
    public void SendOrStop()
    {
        if (IsBusy)
            Stop();
        else
            _ = SendAsync(null);
    }

    public void Stop() => _cts?.Cancel();

    public void NewChat()
    {
        Stop();
        Messages.Clear();
        _lastSentContext = null;
        StatusText = string.Empty;
        RefreshSettings();
        ScheduleContextEstimate();
    }

    public static readonly (string Label, string Prompt)[] QuickActions =
    {
        ("Compare Tabs", "Compare the attached files. Highlight the logical, structural and code differences, then summarize what the differences mean in practice."),
        ("Explain", "Explain what the attached file(s) do: purpose, main parts and how they work together."),
        ("Find Bugs", "Review the attached code for bugs, edge cases and risky patterns. For each issue give the location, why it is a problem and a suggested fix."),
        ("Summarize", "Summarize the attached content concisely."),
    };

    public void RunQuickAction(string label)
    {
        var action = QuickActions.FirstOrDefault(a => a.Label == label);
        if (action.Prompt == null || IsBusy)
            return;

        if (label == "Compare Tabs")
        {
            // Fewer than two tabs ticked: compare every tab that has a file
            if (TabItems.Count(i => i.IsSelected) < 2)
            {
                foreach (var item in TabItems)
                    item.IsSelected = true;
            }
            var (files, _) = GatherSources();
            if (files.Count < 2)
            {
                StatusText = "Compare Tabs needs at least two files: open a file in two tabs (or add files to the Snap List).";
                return;
            }
        }

        _ = SendAsync(action.Prompt);
    }

    private async Task SendAsync(string? quickPrompt)
    {
        string question = (quickPrompt ?? InputText).Trim();
        if (question.Length == 0 || IsBusy)
            return;

        var settings = AiKeyStore.ResolveSettings(_configService.Current);
        var provider = AiProviders.Get(settings.Provider);
        if (provider.RequiresApiKey && string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            StatusText = $"No API key for {provider.DisplayName}: add it in Settings → AI Assistant (click the model badge).";
            return;
        }

        IsBusy = true;
        StatusText = string.Empty;
        AiMessage? reply = null;
        AiMessage? userMessage = null;

        try
        {
            AiContextResult context;
            try
            {
                context = await BuildContextAsync();
            }
            catch (Exception ex)
            {
                StatusText = $"Cannot read the selected context: {ex.Message}";
                return;
            }

            int max = MaxContextChars;
            if (context.CharCount > max)
            {
                StatusText = $"The selected context is {context.CharCount:N0} characters, over the limit of {max:N0}. " +
                             "Deselect some sources or raise the limit in Settings → AI Assistant.";
                return;
            }

            bool attach = !context.IsEmpty && context.Text != _lastSentContext;
            string summary = attach
                ? context.Summary
                : context.IsEmpty ? string.Empty : "📎 same context as before (not sent again)";

            userMessage = new AiMessage
            {
                Role = AiMessageRole.User,
                Content = question,
                ApiContent = attach ? $"{context.Text}\n\n{question}" : question,
                ContextSummary = summary
            };
            Messages.Add(userMessage);
            if (quickPrompt == null)
                InputText = string.Empty;

            if (context.Skipped.Count > 0)
                StatusText = "Skipped: " + string.Join("; ", context.Skipped.Take(3)) +
                             (context.Skipped.Count > 3 ? $" (+{context.Skipped.Count - 3} more)" : string.Empty);

            // History: every finished turn, without failed exchanges
            var turns = Messages
                .Where(m => !m.ExcludeFromHistory && !string.IsNullOrWhiteSpace(m.Content))
                .Select(m => new AiChatTurn(m.IsUser, m.IsUser ? m.ApiContent ?? m.Content : m.Content))
                .ToList();

            reply = new AiMessage { Role = AiMessageRole.Assistant, IsStreaming = true };
            Messages.Add(reply);
            ConversationUpdated?.Invoke(this, EventArgs.Empty);

            _cts = new CancellationTokenSource();
            var sb = new StringBuilder();
            var throttle = Stopwatch.StartNew();

            try
            {
                await foreach (var chunk in _aiService.StreamChatAsync(settings, AiContextBuilder.SystemPrompt, turns, _cts.Token))
                {
                    sb.Append(chunk);
                    // Repaint at most every ~60 ms: per-token updates make long replies stutter
                    if (throttle.ElapsedMilliseconds >= 60)
                    {
                        reply.Content = sb.ToString();
                        ConversationUpdated?.Invoke(this, EventArgs.Empty);
                        throttle.Restart();
                    }
                }

                reply.Content = sb.ToString();
                if (string.IsNullOrWhiteSpace(reply.Content))
                    reply.Content = "(empty reply)";
                if (attach)
                    _lastSentContext = context.Text;
            }
            catch (OperationCanceledException)
            {
                reply.Content = sb.Length > 0 ? sb + "\n\n[Stopped]" : "[Stopped]";
                if (sb.Length == 0)
                    MarkFailed(userMessage, reply);
                else if (attach)
                    _lastSentContext = context.Text;
            }
            catch (AiServiceException ex)
            {
                reply.Content = ex.Message;
                reply.IsError = true;
                MarkFailed(userMessage, reply);
            }
            catch (Exception ex)
            {
                AppLogger.Error($"[AiChat] Request failed: {ex.Message}", ex);
                reply.Content = $"Unexpected error: {ex.Message}";
                reply.IsError = true;
                MarkFailed(userMessage, reply);
            }
        }
        finally
        {
            if (reply != null)
                reply.IsStreaming = false;
            _cts?.Dispose();
            _cts = null;
            IsBusy = false;
            ConversationUpdated?.Invoke(this, EventArgs.Empty);
            ScheduleContextEstimate();
        }
    }

    /// <summary>A failed exchange stays visible but is not sent again as history.</summary>
    private static void MarkFailed(AiMessage? user, AiMessage reply)
    {
        if (user != null)
            user.ExcludeFromHistory = true;
        reply.ExcludeFromHistory = true;
    }

    // ==================== REPLY ACTIONS ====================

    /// <summary>The whole conversation as Markdown (questions, context summaries and replies), for "Export chat".</summary>
    public string BuildConversationMarkdown()
    {
        var settings = AiKeyStore.ResolveSettings(_configService.Current);
        var sb = new StringBuilder();
        sb.Append("# AI Assistant chat - ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm")).Append("\n\n");
        sb.Append("Model: `").Append(settings.Model).Append("` (").Append(AiProviders.Get(settings.Provider).DisplayName).Append(")\n\n");
        foreach (var m in Messages)
        {
            sb.Append("---\n\n");
            sb.Append(m.IsUser ? "## You" : m.IsError ? "## Error" : "## AI").Append(" · ").Append(m.TimeText).Append("\n\n");
            if (m.HasContextSummary)
                sb.Append('*').Append(m.ContextSummary).Append("*\n\n");
            sb.Append(TextFileService.NormalizeNewlines(m.Content).TrimEnd()).Append("\n\n");
        }
        return sb.ToString().Replace("\n", "\r\n");
    }

    private static readonly Regex FencedBlock = new(@"```[^\n]*\n(.*?)\n?```", RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>The single fenced code block of a reply, if it has exactly one; otherwise the whole reply.</summary>
    private static (string Text, bool IsCodeBlock) ExtractPayload(string reply)
    {
        var matches = FencedBlock.Matches(reply);
        return matches.Count == 1 ? (matches[0].Groups[1].Value, true) : (reply, false);
    }

    private void SetTabStatus(string message)
    {
        StatusText = message;
        if (_main.ActiveTab != null)
            _main.ActiveTab.StatusMessage = message;
    }

    /// <summary>Saves the reply as a timestamped .md file in the active tab's folder and opens it.</summary>
    public void SaveReplyAsMarkdown(AiMessage message)
    {
        var tab = _main.ActiveTab;
        if (tab == null || string.IsNullOrWhiteSpace(tab.CurrentPath) || !Directory.Exists(tab.CurrentPath))
        {
            StatusText = "No folder open in the active tab.";
            return;
        }

        try
        {
            string name = $"AI_Reply_{DateTime.Now:yyyyMMdd_HHmmss}.md";
            string path = Path.Combine(tab.CurrentPath, name);
            string text = TextFileService.NormalizeNewlines(message.Content).Replace("\n", "\r\n");
            File.WriteAllText(path, text, new UTF8Encoding(false));
            AppLogger.Info($"[AiChat] Reply saved as {path}");

            tab.Refresh();
            if (tab.IsEditorDirty)
            {
                SetTabStatus($"Saved '{name}' (not opened: the current file has unsaved changes)");
            }
            else
            {
                tab.OpenFile(path);
                SetTabStatus($"Saved and opened '{name}'");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error($"[AiChat] Save as .md failed: {ex.Message}", ex);
            StatusText = $"Cannot save the reply: {ex.Message}";
        }
    }

    /// <summary>
    /// Puts the reply in the active tab's editor in edit mode, unsaved, so the user reviews it before Ctrl+S.
    /// A reply with exactly one code block sends only the code.
    /// </summary>
    public void SendReplyToEditor(AiMessage message, bool append)
    {
        var tab = _main.ActiveTab;
        if (tab == null || !TabHasFile(tab))
        {
            StatusText = "Open a file in the active tab first.";
            return;
        }

        if (!tab.IsPreviewUnlocked)
        {
            if (tab.CanUnlockPreview)
                tab.UnlockPreview();
            if (!tab.IsPreviewUnlocked)
            {
                StatusText = "This file cannot be opened in the editor.";
                return;
            }
        }

        if (!tab.IsEditAllowed)
        {
            StatusText = "This file type is preview-only: add its extension in Settings to edit it.";
            return;
        }

        if (tab.IsEditorLocked)
            tab.UnlockEditor();
        if (tab.IsEditorLocked)
            return;

        var (payload, isCode) = ExtractPayload(TextFileService.NormalizeNewlines(message.Content));
        if (append)
        {
            string current = TextFileService.NormalizeNewlines(tab.EditorText).TrimEnd('\n');
            tab.EditorText = current.Length == 0 ? payload : $"{current}\n\n{payload}";
        }
        else
        {
            tab.EditorText = payload;
        }

        string what = isCode ? "code block" : "reply";
        SetTabStatus(append
            ? $"AI {what} appended to '{tab.CurrentEditingFileName}' - review it, then Ctrl+S to save"
            : $"AI {what} replaced the content of '{tab.CurrentEditingFileName}' - review it, then Ctrl+S to save");
    }
}

/// <summary>One row of the tab list in the AI context selector.</summary>
public sealed partial class AiTabContextItem : ObservableObject
{
    public WorkspaceTabViewModel Tab { get; }
    public string Title { get; }
    public string FileName { get; }
    public bool IsActive { get; }

    [ObservableProperty]
    private bool _isSelected;

    public AiTabContextItem(WorkspaceTabViewModel tab, string title, string fileName, bool isActive, bool isSelected)
    {
        Tab = tab;
        Title = title;
        FileName = fileName;
        IsActive = isActive;
        _isSelected = isSelected;
    }

    public string ToolTip => Tab.CurrentEditingFilePath ?? string.Empty;
}
