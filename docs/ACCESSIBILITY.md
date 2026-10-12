# Accessibility & localization notes

## Localization

- Suite string table: `Source/DiskUtilities.Shared/Localization/en.json` via `SuiteResources.Get(key)`.
- SuiteLauncher applies en strings + `AutomationProperties` on key controls.
- Additional cultures: add `Localization/{culture}.json` as an embedded resource (or beside the Shared DLL) and call `SuiteResources.EnsureLoaded("de")`.
- Per-app XAML still contains many inline strings; migrate hot paths to `SuiteResources` or WinUI `.resw` incrementally.

## Keyboard

| App | Notes |
|-----|--------|
| SuiteLauncher | AccessKeys: Theme **T**, Install menus **I**, Remove menus **R**; standard Tab order through controls |
| Other apps | Prefer system Tab / arrow navigation; destructive dialogs keep Cancel as default |

## Automation

Use `AccessibilityUi.SetName` / `SetHelpText` for controls that lack visible labels (icon-only buttons, list hosts).

## High contrast / themes

- Apps use WinUI theme brushes (`ThemeResource`) and suite theme seed (system / light / dark).
- Prefer `ThemeResource` over hard-coded colors for new UI; diagram colors remain user-configurable in DiskEraser Settings.

## Checklist for new UI

1. Meaningful name (label or `AutomationProperties.Name`)
2. Keyboard reachable (Tab / AccessKey)
3. Destructive actions: Cancel default + acknowledgement when data loss
4. Status text not color-only (include words, not just red/green)
