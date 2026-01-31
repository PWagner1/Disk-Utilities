namespace DiskHealth.Models;

/// <summary>
/// Simple drive info for the drive selection combo.
/// </summary>
public class ScannableDrive
{
    public string Letter { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string DisplayText => string.IsNullOrEmpty(Label)
        ? $"{Letter} (No label)"
        : $"{Letter} ({Label})";
}
