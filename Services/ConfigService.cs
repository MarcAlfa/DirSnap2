using System.Diagnostics;
using System.IO;
using System.Text.Json;
using DirSnap2.Models;

namespace DirSnap2.Services;

public interface IConfigService
{
    AppConfig Current { get; }
    event Action? ConfigChanged;
    void Load();
    void Save();
    void NotifyChanged();
    void AddRecent(string path);
    void RemoveRecent(string path);
    void AddFavorite(string path);
    void RemoveFavorite(string path);
    bool IsFavorite(string path);
}

public sealed class ConfigService : IConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _configPath;
    private readonly string _exampleConfigPath;
    private readonly object _lock = new();

    public AppConfig Current { get; private set; } = new();
    public event Action? ConfigChanged;

    public void NotifyChanged()
    {
        ConfigChanged?.Invoke();
    }

    public ConfigService(string? basePath = null)
    {
        var root = basePath ?? AppContext.BaseDirectory;
        _configPath = Path.Combine(root, "config.json");
        _exampleConfigPath = Path.Combine(root, "config.example.json");
    }

    public void Load()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    var json = File.ReadAllText(_configPath);
                    var loaded = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
                    if (loaded != null)
                    {
                        Current = loaded;
                        SanitizeConfig();
                        return;
                    }
                }

                if (File.Exists(_exampleConfigPath))
                {
                    var json = File.ReadAllText(_exampleConfigPath);
                    var loaded = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
                    if (loaded != null)
                    {
                        Current = loaded;
                        SanitizeConfig();
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ConfigService] Error loading config: {ex.Message}");
            }

            Current = new AppConfig();
            SanitizeConfig();
        }
    }

    private void SanitizeConfig()
    {
        if (Current.Workspace.Tabs.Count == 0)
        {
            Current.Workspace.Tabs.Add(new WorkspaceTabState
            {
                Path = Directory.Exists(Current.LastPath) ? Current.LastPath : "C:\\",
                Mode = "flat"
            });
            Current.Workspace.Active = 0;
        }

        if (Current.Workspace.Active < 0 || Current.Workspace.Active >= Current.Workspace.Tabs.Count)
        {
            Current.Workspace.Active = 0;
        }

        if (Current.Favorites.Count == 0)
        {
            Current.Favorites.Add("C:\\");
        }

        if (Current.EditorExtensions.Count == 0)
        {
            Current.EditorExtensions.AddRange(new[] { ".md", ".txt", ".json" });
        }
    }

    public void Save()
    {
        lock (_lock)
        {
            try
            {
                var json = JsonSerializer.Serialize(Current, JsonOptions);
                var dir = Path.GetDirectoryName(_configPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var tempFile = Path.Combine(dir ?? string.Empty, $".config_{Guid.NewGuid():N}.tmp");
                File.WriteAllText(tempFile, json);
                File.Move(tempFile, _configPath, overwrite: true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ConfigService] Error saving config: {ex.Message}");
            }
        }
    }

    public static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;

        var trimmed = path.Trim();
        if (trimmed.Length == 2 && char.IsLetter(trimmed[0]) && trimmed[1] == ':')
        {
            trimmed += "\\";
        }

        try
        {
            var full = Path.GetFullPath(trimmed);
            return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return trimmed.TrimEnd('\\', '/');
        }
    }

    public void AddRecent(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        lock (_lock)
        {
            var norm = NormalizePath(path);
            Current.Recents.RemoveAll(r => string.Equals(NormalizePath(r), norm, StringComparison.OrdinalIgnoreCase));
            Current.Recents.Insert(0, path);

            if (Current.Recents.Count > 20)
            {
                Current.Recents.RemoveRange(20, Current.Recents.Count - 20);
            }

            Current.LastPath = path;
        }
    }

    public void RemoveRecent(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        lock (_lock)
        {
            var norm = NormalizePath(path);
            Current.Recents.RemoveAll(r => string.Equals(NormalizePath(r), norm, StringComparison.OrdinalIgnoreCase));
        }
    }

    public bool IsFavorite(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        lock (_lock)
        {
            var norm = NormalizePath(path);
            return Current.Favorites.Any(f => string.Equals(NormalizePath(f), norm, StringComparison.OrdinalIgnoreCase));
        }
    }

    public void AddFavorite(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        lock (_lock)
        {
            if (!IsFavorite(path))
            {
                var toAdd = path.Trim();
                if (toAdd.Length == 2 && char.IsLetter(toAdd[0]) && toAdd[1] == ':')
                {
                    toAdd += "\\";
                }
                Current.Favorites.Add(toAdd);
            }
        }
    }

    public void RemoveFavorite(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        lock (_lock)
        {
            var norm = NormalizePath(path);
            Current.Favorites.RemoveAll(f => string.Equals(NormalizePath(f), norm, StringComparison.OrdinalIgnoreCase));
        }
    }
}
