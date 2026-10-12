namespace DiskHealth.Models;

/// <summary>How a recoverable candidate was discovered.</summary>
public enum RecoverySourceKind
{
    /// <summary>Raw signature carving (not undelete).</summary>
    Carve = 0,
    /// <summary>Windows Recycle Bin ($I / $R) metadata.</summary>
    RecycleBin = 1,
    /// <summary>NTFS MFT record marked unused (deleted) with residual attributes.</summary>
    MftDeleted = 2
}

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

    /// <summary>Heuristic recovery estimate: 0–100 (High=80+, Medium=50–79, Low=&lt;50). Not a guarantee.</summary>
    public int RecoveryChancePercent { get; set; }

    /// <summary>Human-readable heuristic recovery chance.</summary>
    public string RecoveryChanceLabel => RecoveryChancePercent >= 80 ? "High (est.)"
        : RecoveryChancePercent >= 50 ? "Medium (est.)" : "Low (est.)";

    /// <summary>Starting offset on disk (for carve / MFT data runs).</summary>
    public long Offset { get; set; }

    /// <summary>Path hint: Recycle $R path, original path, or MFT name.</summary>
    public string? PathHint { get; set; }

    /// <summary>Discovery method — keep carve vs undelete distinct in the UI.</summary>
    public RecoverySourceKind SourceKind { get; set; } = RecoverySourceKind.Carve;

    /// <summary>Short label for list UI.</summary>
    public string SourceLabel => SourceKind switch
    {
        RecoverySourceKind.RecycleBin => "Recycle Bin",
        RecoverySourceKind.MftDeleted => "MFT deleted",
        _ => "Carve"
    };

    /// <summary>Non-resident data run list as (lcn, clusterCount) for MFT recovery; null if N/A.</summary>
    public IReadOnlyList<(long Lcn, long Clusters)>? DataRuns { get; set; }

    /// <summary>Bytes per cluster when <see cref="DataRuns"/> is set.</summary>
    public int BytesPerCluster { get; set; }

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
