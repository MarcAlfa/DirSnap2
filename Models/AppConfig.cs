using System.Text.Json.Serialization;

namespace DirSnap2.Models;

public sealed class WindowPlacement
{
    [JsonPropertyName("x")]
    public int X { get; set; } = 100;

    [JsonPropertyName("y")]
    public int Y { get; set; } = 100;

    [JsonPropertyName("width")]
    public int Width { get; set; } = 1400;

    [JsonPropertyName("height")]
    public int Height { get; set; } = 850;

    [JsonPropertyName("maximized")]
    public bool Maximized { get; set; }

    [JsonPropertyName("monitor_index")]
    public int MonitorIndex { get; set; }
}

public sealed class WorkspaceTabState
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "C:\\";

    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "flat"; // "flat" or "tree"

    [JsonPropertyName("filter")]
    public string Filter { get; set; } = string.Empty;

    [JsonPropertyName("show_all")]
    public bool ShowAll { get; set; }

    [JsonPropertyName("sort_col")]
    public string SortCol { get; set; } = "name"; // "name", "size", "mtime"

    [JsonPropertyName("sort_desc")]
    public bool SortDesc { get; set; }

    [JsonPropertyName("selected_file")]
    public string? SelectedFile { get; set; }

    [JsonPropertyName("editor_wrap")]
    public bool EditorWrap { get; set; } = true;
}

public sealed class WorkspaceState
{
    [JsonPropertyName("tabs")]
    public List<WorkspaceTabState> Tabs { get; set; } = new();

    [JsonPropertyName("active")]
    public int Active { get; set; }
}

public sealed class AppConfig
{
    [JsonPropertyName("theme")]
    public string Theme { get; set; } = "dark"; // "dark" or "light"

    [JsonPropertyName("default_color_theme")]
    public string DefaultColorTheme { get; set; } = "blue";

    [JsonPropertyName("last_path")]
    public string LastPath { get; set; } = "C:\\";

    [JsonPropertyName("include_full_path")]
    public bool IncludeFullPath { get; set; }

    [JsonPropertyName("include_extension")]
    public bool IncludeExtension { get; set; } = true;

    [JsonPropertyName("favorites")]
    public List<string> Favorites { get; set; } = new() { "C:\\" };

    [JsonPropertyName("recents")]
    public List<string> Recents { get; set; } = new();

    [JsonPropertyName("editor_extensions")]
    public List<string> EditorExtensions { get; set; } = new() { ".md", ".txt", ".json" };

    [JsonPropertyName("editor_wrap")]
    public bool EditorWrap { get; set; } = true;

    [JsonPropertyName("splitter_distance")]
    public double SplitterDistance { get; set; } = 450;

    [JsonPropertyName("window")]
    public WindowPlacement Window { get; set; } = new();

    [JsonPropertyName("workspace")]
    public WorkspaceState Workspace { get; set; } = new();
}
