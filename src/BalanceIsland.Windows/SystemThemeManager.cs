using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;
using WpfSystemColors = System.Windows.SystemColors;

namespace BalanceIsland.Windows;

/// <summary>Maps the Windows app theme to WPF dynamic resources and the native title bar.</summary>
public sealed class SystemThemeManager : IDisposable
{
    private const string PersonalizeKey =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const int DwmUseImmersiveDarkModeBefore20H1 = 19;
    private const int DwmUseImmersiveDarkMode = 20;

    private readonly ResourceDictionary _resources;
    private readonly Dispatcher _dispatcher;
    private readonly List<WeakReference<Window>> _windows = [];
    private bool _isDark;

    public SystemThemeManager(ResourceDictionary resources, Dispatcher dispatcher)
    {
        _resources = resources;
        _dispatcher = dispatcher;
        ApplyTheme();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public void Track(Window window)
    {
        _windows.Add(new WeakReference<Window>(window));
        window.SourceInitialized += (_, _) => ApplyNativeTitleBar(window);
        if (new WindowInteropHelper(window).Handle != IntPtr.Zero) ApplyNativeTitleBar(window);
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_dispatcher.HasShutdownStarted) return;
        _dispatcher.BeginInvoke(ApplyTheme);
    }

    private void ApplyTheme()
    {
        _isDark = IsWindowsDarkMode();
        if (SystemParameters.HighContrast)
        {
            Set("WindowBackground", WpfSystemColors.WindowColor);
            Set("CardBackground", WpfSystemColors.ControlColor);
            Set("ControlBackground", WpfSystemColors.WindowColor);
            Set("ControlHoverBackground", WpfSystemColors.ControlLightColor);
            Set("ControlPressedBackground", WpfSystemColors.ControlDarkColor);
            Set("ControlDisabledBackground", WpfSystemColors.ControlColor);
            Set("ControlBorder", WpfSystemColors.ActiveBorderColor);
            Set("PrimaryText", WpfSystemColors.WindowTextColor);
            Set("SecondaryText", WpfSystemColors.GrayTextColor);
            Set("DisabledText", WpfSystemColors.GrayTextColor);
            Set("AccentBrush", WpfSystemColors.HighlightColor);
            Set("AccentHoverBrush", WpfSystemColors.HotTrackColor);
            Set("SelectionBrush", WpfSystemColors.HighlightColor);
            Set("GridRowBackground", WpfSystemColors.WindowColor);
            Set("GridAlternateRowBackground", WpfSystemColors.ControlColor);
            Set("IslandBackground", WpfSystemColors.WindowColor);
        }
        else if (_isDark)
        {
            Set("WindowBackground", "#101419");
            Set("CardBackground", "#1A2027");
            Set("ControlBackground", "#202934");
            Set("ControlHoverBackground", "#2A3643");
            Set("ControlPressedBackground", "#344354");
            Set("ControlDisabledBackground", "#252C34");
            Set("ControlBorder", "#465463");
            Set("PrimaryText", "#F3F6F9");
            Set("SecondaryText", "#AAB4BF");
            Set("DisabledText", "#707B87");
            Set("AccentBrush", "#6EA8FE");
            Set("AccentHoverBrush", "#8BBBFF");
            Set("SelectionBrush", "#315B86");
            Set("GridRowBackground", "#1A2027");
            Set("GridAlternateRowBackground", "#171D24");
            Set("IslandBackground", "#E61A1F26");
        }
        else
        {
            Set("WindowBackground", "#F4F6F8");
            Set("CardBackground", "#FFFFFF");
            Set("ControlBackground", "#F8FAFC");
            Set("ControlHoverBackground", "#EDF2F7");
            Set("ControlPressedBackground", "#E2E8F0");
            Set("ControlDisabledBackground", "#E9EDF2");
            Set("ControlBorder", "#C5CDD7");
            Set("PrimaryText", "#17202A");
            Set("SecondaryText", "#5D6875");
            Set("DisabledText", "#98A2AE");
            Set("AccentBrush", "#2563A9");
            Set("AccentHoverBrush", "#1E518D");
            Set("SelectionBrush", "#CFE4FA");
            Set("GridRowBackground", "#FFFFFF");
            Set("GridAlternateRowBackground", "#F7F9FB");
            Set("IslandBackground", "#EFFFFFFF");
        }

        for (var index = _windows.Count - 1; index >= 0; index--)
        {
            if (_windows[index].TryGetTarget(out var window)) ApplyNativeTitleBar(window);
            else _windows.RemoveAt(index);
        }
    }

    private static bool IsWindowsDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch
        {
            return false;
        }
    }

    private void ApplyNativeTitleBar(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        var enabled = _isDark && !SystemParameters.HighContrast ? 1 : 0;
        if (DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
            DwmSetWindowAttribute(handle, DwmUseImmersiveDarkModeBefore20H1, ref enabled, sizeof(int));
    }

    private void Set(string key, string color) => Set(key,
        (MediaColor)MediaColorConverter.ConvertFromString(color));

    private void Set(string key, MediaColor color) => _resources[key] = new SolidColorBrush(color);

    public void Dispose()
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr window, int attribute, ref int attributeValue, int attributeSize);
}
