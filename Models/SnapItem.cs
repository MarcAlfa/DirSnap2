using CommunityToolkit.Mvvm.ComponentModel;

namespace DirSnap2.Models;

/// <summary>
/// Represents an item saved in the global Snap List cart.
/// </summary>
public sealed class SnapItem : ObservableObject
{
    private string _name = string.Empty;
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    private string _path = string.Empty;
    public string Path
    {
        get => _path;
        set => SetProperty(ref _path, value);
    }

    private bool _isDirectory;
    public bool IsDirectory
    {
        get => _isDirectory;
        set => SetProperty(ref _isDirectory, value);
    }

    private long _size;
    public long Size
    {
        get => _size;
        set => SetProperty(ref _size, value);
    }

    private DateTime _modifiedTime;
    public DateTime ModifiedTime
    {
        get => _modifiedTime;
        set => SetProperty(ref _modifiedTime, value);
    }

    private DateTime _addedTime = DateTime.Now;
    public DateTime AddedTime
    {
        get => _addedTime;
        set => SetProperty(ref _addedTime, value);
    }

    public string FormattedSize =>
        IsDirectory ? string.Empty : FSItem.FormatSize(Size);

    public string Extension =>
        IsDirectory ? string.Empty : System.IO.Path.GetExtension(Name).ToLowerInvariant();

    public string Glyph =>
        new FSItem { Name = Name, Path = Path, IsDirectory = IsDirectory }.Glyph;

    public Microsoft.UI.Xaml.Media.SolidColorBrush GlyphBrush =>
        new FSItem { Name = Name, Path = Path, IsDirectory = IsDirectory }.GlyphBrush;

    public string Format(bool fullPath, bool withExtension)
    {
        if (IsDirectory)
            return fullPath ? Path : Name;

        if (fullPath)
        {
            if (withExtension)
                return Path;

            var dir = System.IO.Path.GetDirectoryName(Path);
            var stem = System.IO.Path.GetFileNameWithoutExtension(Path);
            return string.IsNullOrEmpty(dir) ? stem : System.IO.Path.Combine(dir, stem);
        }
        else
        {
            return withExtension ? Name : System.IO.Path.GetFileNameWithoutExtension(Name);
        }
    }

    public static SnapItem FromFSItem(FSItem item)
    {
        return new SnapItem
        {
            Name = item.Name,
            Path = item.Path,
            IsDirectory = item.IsDirectory,
            Size = item.Size,
            ModifiedTime = item.ModifiedTime,
            AddedTime = DateTime.Now
        };
    }
}
