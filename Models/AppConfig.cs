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
    public string SortCol { get; set; } = "mtime"; // "name", "size", "mtime" (default: last modified first)

    [JsonPropertyName("sort_desc")]
    public bool SortDesc { get; set; } = true;

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

    [JsonPropertyName("editor_font_size")]
    public double EditorFontSize { get; set; } = 13;

    [JsonPropertyName("outline_panel_open")]
    public bool OutlinePanelOpen { get; set; } = true;

    [JsonPropertyName("outline_panel_width")]
    public double OutlinePanelWidth { get; set; } = 230;

    [JsonPropertyName("left_panel_width")]
    public double LeftPanelWidth { get; set; } = 480;

    [JsonPropertyName("folder_peek_height")]
    public double FolderPeekHeight { get; set; } = 160;

    [JsonPropertyName("folder_peek_open")]
    public bool FolderPeekOpen { get; set; } = true;

    /// <summary>One-time switch (v0.1.1) of the saved tabs to the new default sort: last modified first.</summary>
    [JsonPropertyName("sort_mtime_default_applied")]
    public bool SortMtimeDefaultApplied { get; set; }

    [JsonPropertyName("find_match_case")]
    public bool FindMatchCase { get; set; }

    [JsonPropertyName("file_search_subfolders")]
    public bool FileSearchSubfolders { get; set; } = true;

    [JsonPropertyName("file_search_match_case")]
    public bool FileSearchMatchCase { get; set; }

    [JsonPropertyName("splitter_distance")]
    public double SplitterDistance { get; set; } = 450;

    [JsonPropertyName("window")]
    public WindowPlacement Window { get; set; } = new();

    [JsonPropertyName("workspace")]
    public WorkspaceState Workspace { get; set; } = new();
}
