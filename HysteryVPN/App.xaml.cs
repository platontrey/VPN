using System.Configuration;
using System.Data;
using System.Windows;
using Microsoft.Win32;
using Application = System.Windows.Application;

namespace HysteryVPN;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        bool isDark = IsSystemThemeDark();
        string themeUri = isDark ? "Themes/DarkTheme.xaml" : "Themes/LightTheme.xaml";
        Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary() { Source = new Uri(themeUri!, UriKind.Relative) });
    }

    private bool IsSystemThemeDark()
    {
        try
        {
            using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
            {
                if (key != null)
                {
                    object? value = key.GetValue("AppsUseLightTheme");
                    if (value != null)
                    {
                        return (int)value == 0;
                    }
                }
            }
        }
        catch
        {
            // Ignore
        }
        return true; // Default to dark
    }
}