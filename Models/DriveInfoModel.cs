namespace DirSnap2.Models;

/// <summary>
/// Information regarding a Windows storage unit.
/// </summary>
public sealed class DriveInfoModel
{
    public string Letter { get; set; } = string.Empty;       // E.g. "C:"
    public string Path { get; set; } = string.Empty;         // E.g. "C:\"
    public string Label { get; set; } = string.Empty;        // E.g. "Windows"
    public string DriveType { get; set; } = "Fixed";         // E.g. "Fixed", "Removable", "Network"

    public string DisplayName =>
        !string.IsNullOrWhiteSpace(Label) ? $"{Letter} [{Label}]" : $"{Letter} [Drive]";

    public override string ToString() => DisplayName;
}
