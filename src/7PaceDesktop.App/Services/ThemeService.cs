using System.Windows;
using Microsoft.Win32;
using PaceDesktop.Core.Storage;

namespace PaceDesktop.App.Services;

/// <summary>Applies a ThemePreference by swapping the merged Palette dictionary.
/// For System, resolves the current Windows app theme from the registry.</summary>
public sealed class ThemeService
{
    public void Apply(ThemePreference preference)
    {
        var effective = preference switch
        {
            ThemePreference.Light => "Light",
            ThemePreference.Dark => "Dark",
            _ => SystemUsesLightTheme() ? "Light" : "Dark",
        };

        var newPalette = new ResourceDictionary
        {
            Source = new Uri($"Themes/Palette.{effective}.xaml", UriKind.Relative)
        };

        var merged = Application.Current.Resources.MergedDictionaries;
        for (var i = merged.Count - 1; i >= 0; i--)
        {
            var src = merged[i].Source?.OriginalString ?? string.Empty;
            if (src.Contains("Palette.")) merged.RemoveAt(i);
        }
        // Insert the palette before the styles dictionary so DynamicResource lookups resolve.
        merged.Insert(0, newPalette);
    }

    private static bool SystemUsesLightTheme()
    {
        try
        {
            var value = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 1);
            return value is int i ? i != 0 : true;
        }
        catch
        {
            return true;
        }
    }
}
