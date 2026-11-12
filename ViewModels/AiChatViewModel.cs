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

    private readonly HashSet<WorkspaceTabViewModel> _extraTabs = new();
    private WorkspaceTabViewModel? _watchedTab;
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
    private bool _includeCurrentTab = true;

    [ObservableProperty]
    private bool _includeSnapList;

    [ObservableProperty]
    private bool _includeFolderList;

    [ObservableProperty]
    private string _currentTabLabel = "Current Tab: (none)";

    [ObservableProperty]
    private string _extraTabsLabel = "Add Tab…";

    [ObservableProperty]
    private string _snapListLabel = "Snap List (0)";

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

        _main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.ActiveTab))
            {
                WatchActiveTab();
                RefreshContextLabels();
            }
        };
        _main.Tabs.CollectionChanged += (_, _) => RefreshContextLabels();
        _main.SnapList.ItemsChanged += (_, _) => RefreshContextLabels();

        WatchActiveTab();
        RefreshSettings();
        RefreshContextLabels();
    }

    // ==================== LABELS & SETTINGS ====================

    /// <summary>Re-reads provider and model from the config (after Settings closes).</summary>
    public void RefreshSettings()
    {
        var s = AiKeyStore.ResolveSettings(_configService.Current);
        var provider = AiProviders.Get(s.Provider);
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

    private void WatchActiveTab()
    {
        if (_watchedTab != null)
            _watchedTab.PropertyChanged -= OnWatchedTabPropertyChanged;
        _watchedTab = _main.ActiveTab;
        if (_watchedTab != null)
            _watchedTab.PropertyChanged += OnWatchedTabPropertyChanged;
    }

    private void OnWatchedTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkspaceTabViewModel.CurrentEditingFilePath)
            or nameof(WorkspaceTabViewModel.HasActiveFile)
            or nameof(WorkspaceTabViewModel.CurrentPath))
        {
            RefreshContextLabels();
        }
    }

    partial void OnIncludeCurrentTabChanged(bool value) => ScheduleContextEstimate();
    partial void OnIncludeSnapListChanged(bool value) => ScheduleContextEstimate();
    partial void OnIncludeFolderListChanged(bool value) => ScheduleContextEstimate();

    private string TabName(WorkspaceTabViewModel tab) => $"Tab {_main.Tabs.IndexOf(tab) + 1}";

    private static bool TabHasFile(WorkspaceTabViewModel tab) =>
        tab.HasActiveFile && !string.IsNullOrEmpty(tab.CurrentEditingFilePath);

    public void RefreshContextLabels()
    {
        var active = _main.ActiveTab;
        CurrentTabLabel = active != null && TabHasFile(active)
            ? $"{TabName(active)}: {Path.GetFileName(active.CurrentEditingFilePath)}"
            : "Current Tab: (none)";

        _extraTabs.RemoveWhere(t => !_main.Tabs.Contains(t) || t == active || !TabHasFile(t));
        ExtraTabsLabel = _extraTabs.Count switch
        {
            0 => "Add Tab…",
            1 => $"+ {TabName(_extraTabs.First())}",
            _ => $"+ {_extraTabs.Count} tabs"
        };

        int files = _main.SnapList.Items.Count(i => !i.IsDirectory);
        SnapListLabel = $"Snap List ({files})";

        ScheduleContextEstimate();
    }

    /// <summary>Other tabs with an open file, for the "Add Tab…" menu.</summary>
    public IEnumerable<(WorkspaceTabViewModel Tab, string Label, bool IsSelected)> GetOtherTabs()
    {
        foreach (var tab in _main.Tabs)
        {
            if (tab == _main.ActiveTab || !TabHasFile(tab))
                continue;
            yield return (tab, $"{TabName(tab)}: {Path.GetFileName(tab.CurrentEditingFilePath)}", _extraTabs.Contains(tab));
        }
    }

    public void SetExtraTab(WorkspaceTabViewModel tab, bool selected)
    {
        if (selected)
            _extraTabs.Add(tab);
        else
            _extraTabs.Remove(tab);
        RefreshContextLabels();
    }

    // ==================== CONTEXT ====================

    /// <summary>Collects the chosen sources. Must run on the UI thread (reads the editors' text).</summary>
    private (List<AiContextFile> Files, string? FolderPath) GatherSources()
    {
        var files = new List<AiContextFile>();
        var active = _main.ActiveTab;

        AiContextFile FromTab(WorkspaceTabViewModel tab, string source) =>
            new(source, tab.CurrentEditingFilePath!, tab.IsPreviewUnlocked ? tab.EditorText : null);

        if (IncludeCurrentTab && active != null && TabHasFile(active))
            files.Add(FromTab(active, $"{TabName(active)} (Active)"));

        foreach (var tab in _main.Tabs)
        {
            if (_extraTabs.Contains(tab) && tab != active && TabHasFile(tab))
                files.Add(FromTab(tab, TabName(tab)));
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
            // Nothing extra chosen: compare the current tab with every other tab that has a file
            if (_extraTabs.Count == 0)
            {
                foreach (var (tab, _, _) in GetOtherTabs().ToList())
                    _extraTabs.Add(tab);
                IncludeCurrentTab = true;
                RefreshContextLabels();
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
