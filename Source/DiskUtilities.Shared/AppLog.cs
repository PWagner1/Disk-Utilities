namespace DiskUtilities.Shared;

/// <summary>
/// Lightweight suite logger: Debug output plus a daily log file under
/// <c>%LocalAppData%\DiskUtilities\logs\</c>. Never throws.
/// </summary>
public static class AppLog
{
    private static readonly object Gate = new();
    private static string? _lastError;

    /// <summary>Directory for user-visible log files.</summary>
    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DiskUtilities",
        "logs");

    /// <summary>Most recent error message (for UI status); cleared by <see cref="ConsumeLastError"/>.</summary>
    public static string? LastError
    {
        get { lock (Gate) { return _lastError; } }
    }

    /// <summary>Returns and clears <see cref="LastError"/> for display in the UI.</summary>
    public static string? ConsumeLastError()
    {
        lock (Gate)
        {
            string? error = _lastError;
            _lastError = null;
            return error;
        }
    }

    /// <summary>Clears <see cref="LastError"/> without reading it.</summary>
    public static void ClearLastError()
    {
        lock (Gate)
        {
            _lastError = null;
        }
    }

    public static void Info(string message, string? source = null)
        => Write("INFO", message, exception: null, source, recordAsLastError: false);

    public static void Warning(string message, Exception? exception = null, string? source = null)
        => Write("WARN", message, exception, source, recordAsLastError: false);

    public static void Error(string message, Exception? exception = null, string? source = null)
        => Write("ERROR", message, exception, source, recordAsLastError: true);

    private static void Write(
        string level,
        string message,
        Exception? exception,
        string? source,
        bool recordAsLastError)
    {
        var sb = new StringBuilder();
        sb.Append(DateTime.Now.ToString("O"));
        sb.Append(" [").Append(level).Append("] ");
        if (!string.IsNullOrWhiteSpace(source))
        {
            sb.Append(source).Append(": ");
        }

        sb.Append(message);
        if (exception != null)
        {
            sb.Append(" | ").Append(exception.GetType().Name).Append(": ").Append(exception.Message);
        }

        string line = sb.ToString();
        try
        {
            Debug.WriteLine(line);
        }
        catch
        {
            // ignored
        }

        if (recordAsLastError)
        {
            string ui = message;
            if (exception != null && !string.IsNullOrWhiteSpace(exception.Message))
            {
                ui = $"{message}: {exception.Message}";
            }

            lock (Gate)
            {
                _lastError = ui;
            }
        }

        try
        {
            Directory.CreateDirectory(LogDirectory);
            string path = Path.Combine(LogDirectory, $"disk-utilities-{DateTime.Now:yyyyMMdd}.log");
            File.AppendAllText(path, line + Environment.NewLine);
        }
        catch
        {
            // Logging must never throw
        }
    }
}
