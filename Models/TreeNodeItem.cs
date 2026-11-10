using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DirSnap2.Models;

/// <summary>
/// Represents a node in the hierarchical drive tree (left panel tree mode).
/// Supports lazy-loading of child folders and files.
/// </summary>
public sealed class TreeNodeItem : ObservableObject
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

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    private bool _hasUnrealizedChildren = true;
    public bool HasUnrealizedChildren
    {
        get => _hasUnrealizedChildren;
        set => SetProperty(ref _hasUnrealizedChildren, value);
    }

    public ObservableCollection<TreeNodeItem> Children { get; } = new();

    public string Extension =>
        IsDirectory ? string.Empty : System.IO.Path.GetExtension(Name).ToLowerInvariant();

    public string Glyph =>
        IsDirectory ? "\uE8B7" : "\uE8A5";

    public FSItem ToFSItem()
    {
        return new FSItem
        {
            Name = Name,
            Path = Path,
            IsDirectory = IsDirectory,
            Size = Size,
            ModifiedTime = ModifiedTime
        };
    }
}
