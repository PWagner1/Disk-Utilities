namespace DiskSeer.Models;

/// <summary>
/// Represents a breadcrumb navigation item for path display.
/// </summary>
public class BreadcrumbItem
{
    /// <summary>Display name for the breadcrumb.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Full path this breadcrumb represents.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Creates a new breadcrumb item.</summary>
    public BreadcrumbItem(string name, string path)
    {
        Name = name;
        Path = path;
    }
}
