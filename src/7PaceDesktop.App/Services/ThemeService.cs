using System.Windows;
using PaceDesktop.Core.Storage;
using Wpf.Ui.Appearance;

namespace PaceDesktop.App.Services;

/// <summary>Applies a ThemePreference using WPF-UI. For System, follows the OS theme
/// and live-updates via SystemThemeWatcher; for Light/Dark, applies that theme explicitly.</summary>
public sealed class ThemeService
{
    public void Apply(ThemePreference preference, Window window)
    {
        switch (preference)
        {
            case ThemePreference.Light:
                SystemThemeWatcher.UnWatch(window);
                ApplicationThemeManager.Apply(ApplicationTheme.Light);
                break;
            case ThemePreference.Dark:
                SystemThemeWatcher.UnWatch(window);
                ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                break;
            default: // System
                ApplicationThemeManager.ApplySystemTheme();
                SystemThemeWatcher.Watch(window);
                break;
        }
    }
}
