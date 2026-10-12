namespace SuiteLauncher;

/// <summary>View model row for one suite tool in the launcher list.</summary>
public sealed class LauncherAppItem
{
    public SuiteAppId Id { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string ElevationHint { get; init; } = string.Empty;
    public string RelatedHint { get; init; } = string.Empty;
    public string StatusText { get; set; } = string.Empty;
    public Visibility StatusVisible => string.IsNullOrEmpty(StatusText) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility ElevateVisible { get; init; }
}
