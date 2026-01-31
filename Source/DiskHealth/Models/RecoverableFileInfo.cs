using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DiskHealth.Models;

/// <summary>
/// Represents a potentially recoverable file found during a drive scan.
/// </summary>
public class RecoverableFileInfo : INotifyPropertyChanged
{
    /// <summary>Display name (e.g., from signature or "Unknown_001.jpg").</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Detected file type/extension from signature.</summary>
    public string FileType { get; set; } = string.Empty;

    /// <summary>Estimated size in bytes (0 if unknown).</summary>
    public long EstimatedSize { get; set; }

    /// <summary>Recovery probability: 0–100 (High=80+, Medium=50–79, Low=&lt;50).</summary>
    public int RecoveryChancePercent { get; set; }

    /// <summary>Human-readable recovery chance (High, Medium, Low).</summary>
    public string RecoveryChanceLabel => RecoveryChancePercent >= 80 ? "High"
        : RecoveryChancePercent >= 50 ? "Medium" : "Low";

    /// <summary>Starting offset on disk (for recovery service).</summary>
    public long Offset { get; set; }

    /// <summary>Path hint if recovered from filesystem metadata (optional).</summary>
    public string? PathHint { get; set; }

    private bool _isSelected = true;

    /// <summary>Whether this entry is selected for recovery.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
