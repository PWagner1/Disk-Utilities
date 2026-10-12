namespace DiskUtilities.Shared;

/// <summary>Helpers for AutomationProperties and keyboard-friendly defaults.</summary>
public static class AccessibilityUi
{
    /// <summary>Sets AutomationProperties.Name when the control has no accessible name yet.</summary>
    public static void SetName(DependencyObject element, string name)
    {
        if (element == null || string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        try
        {
            string? existing = AutomationProperties.GetName(element);
            if (string.IsNullOrWhiteSpace(existing))
            {
                AutomationProperties.SetName(element, name);
            }
        }
        catch
        {
            // ignore — non-UI threads / unsupported types
        }
    }

    /// <summary>Sets a help text / tooltip-equivalent for assistive tech.</summary>
    public static void SetHelpText(DependencyObject element, string help)
    {
        if (element == null || string.IsNullOrWhiteSpace(help))
        {
            return;
        }

        try
        {
            AutomationProperties.SetHelpText(element, help);
        }
        catch
        {
            // ignore
        }
    }
}
