using CommunityToolkit.Mvvm.ComponentModel;

namespace DirSnap2.Models;

public enum AiMessageRole
{
    User,
    Assistant
}

/// <summary>One bubble of the AI Assistant conversation.</summary>
public sealed partial class AiMessage : ObservableObject
{
    public AiMessageRole Role { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.Now;

    public bool IsUser => Role == AiMessageRole.User;

    /// <summary>Text shown in the bubble (for the user: the question only, without the attached files).</summary>
    [ObservableProperty]
    private string _content = string.Empty;

    /// <summary>
    /// Text sent to the provider for this turn. For a user message it is the attached context plus the
    /// question; null means "same as <see cref="Content"/>".
    /// </summary>
    public string? ApiContent { get; init; }

    /// <summary>Short description of the attached context ("📎 2 files · 34 KB"), empty when none.</summary>
    public string ContextSummary { get; init; } = string.Empty;

    public bool HasContextSummary => !string.IsNullOrEmpty(ContextSummary);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowActions))]
    [NotifyPropertyChangedFor(nameof(ShowThinking))]
    private bool _isStreaming;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowActions))]
    private bool _isError;

    /// <summary>Copy / Save / Send to Editor buttons: only on a finished, successful reply.</summary>
    public bool ShowActions => !IsUser && !IsStreaming && !IsError && !string.IsNullOrWhiteSpace(Content);

    /// <summary>"Thinking…" placeholder while the reply has not started yet.</summary>
    public bool ShowThinking => IsStreaming && string.IsNullOrEmpty(Content);

    partial void OnContentChanged(string value)
    {
        OnPropertyChanged(nameof(ShowActions));
        OnPropertyChanged(nameof(ShowThinking));
    }

    public string TimeText => Timestamp.ToString("HH:mm");

    /// <summary>Failed exchanges stay visible but are not sent again as conversation history.</summary>
    public bool ExcludeFromHistory { get; set; }
}
