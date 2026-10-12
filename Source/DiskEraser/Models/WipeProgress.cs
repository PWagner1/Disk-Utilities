namespace DiskEraser.Models;

/// <summary>Progress for free-space overwrite (not hardware Secure Erase).</summary>
public readonly record struct WipeProgress(
    int Pass,
    int TotalPasses,
    double Percent,
    long BytesWritten,
    long BytesTotal,
    TimeSpan? EstimatedRemaining)
{
    /// <summary>Estimates remaining time from elapsed work and bytes written.</summary>
    public static TimeSpan? EstimateRemaining(TimeSpan elapsed, long bytesWritten, long bytesTotal)
    {
        if (bytesWritten <= 0 || bytesTotal <= bytesWritten || elapsed <= TimeSpan.Zero)
        {
            return null;
        }

        double rate = bytesWritten / elapsed.TotalSeconds;
        if (rate < 1)
        {
            return null;
        }

        double seconds = (bytesTotal - bytesWritten) / rate;
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0 || seconds > TimeSpan.FromDays(7).TotalSeconds)
        {
            return null;
        }

        return TimeSpan.FromSeconds(seconds);
    }

    public string FormatEta()
    {
        if (EstimatedRemaining is not { } eta)
        {
            return "ETA —";
        }

        if (eta.TotalHours >= 1)
        {
            return $"ETA {eta.Hours}h {eta.Minutes}m";
        }

        if (eta.TotalMinutes >= 1)
        {
            return $"ETA {eta.Minutes}m {eta.Seconds}s";
        }

        return $"ETA {Math.Max(1, (int)eta.TotalSeconds)}s";
    }
}
