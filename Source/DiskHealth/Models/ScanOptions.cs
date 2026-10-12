namespace DiskHealth.Models;

/// <summary>
/// Options for signature carving scans.
/// </summary>
public sealed class ScanOptions
{
    /// <summary>When true, stop after <see cref="MaxBytes"/> (quick scan).</summary>
    public bool QuickScan { get; set; }

    /// <summary>Byte cap for quick scans (default 8 GiB).</summary>
    public long MaxBytes { get; set; } = 8L * 1024 * 1024 * 1024;

    /// <summary>
    /// When non-null/non-empty, only keep hits whose extension is in this set (e.g. ".jpg").
    /// Comparison is ordinal-ignore-case.
    /// </summary>
    public IReadOnlySet<string>? AllowedExtensions { get; set; }

    /// <summary>Optional pause gate; waiters block between chunks while paused.</summary>
    public ScanPauseGate? PauseGate { get; set; }
}

/// <summary>Thread-safe pause/resume for long-running scans.</summary>
public sealed class ScanPauseGate
{
    private readonly ManualResetEventSlim _gate = new(initialState: true);

    public bool IsPaused => !_gate.IsSet;

    public void Pause() => _gate.Reset();

    public void Resume() => _gate.Set();

    public void WaitIfPaused(CancellationToken ct) => _gate.Wait(ct);
}
