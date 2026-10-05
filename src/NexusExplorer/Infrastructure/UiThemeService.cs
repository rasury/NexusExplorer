using System.Windows;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;

namespace NexusExplorer.Infrastructure;

public enum UiThemeMode { System, Light, Dark }

public static class UiThemeService
{
    public static UiThemeMode Mode { get; private set; } = UiThemeMode.System;
    public static bool IsDark { get; private set; }
    private static bool _watching;
    public static event Action? Changed;

    public static void Start(UiThemeMode mode)
    {
        Apply(mode);
        if (!_watching) { SystemEvents.UserPreferenceChanged += OnSystemChanged; _watching = true; }
    }
    public static void Stop()
    { if (_watching) SystemEvents.UserPreferenceChanged -= OnSystemChanged; _watching = false; }
    private static void OnSystemChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        var app = System.Windows.Application.Current;
        if (Mode == UiThemeMode.System && app is not null && !app.Dispatcher.HasShutdownStarted)
            _ = app.Dispatcher.BeginInvoke(() => Apply(UiThemeMode.System));
    }
    public static void Apply(UiThemeMode mode, bool save = false)
    {
        Mode = mode;
        IsDark = mode == UiThemeMode.Dark || mode == UiThemeMode.System && Theme.GetSystemTheme() == BaseTheme.Dark;
        var palette = new PaletteHelper(); var theme = palette.GetTheme();
        theme.SetBaseTheme(IsDark ? BaseTheme.Dark : BaseTheme.Light);
        Color C(string value) => (Color)ColorConverter.ConvertFromString(value);
        var primary = IsDark ? "#D0BCFF" : "#6750A4";
        var onPrimary = IsDark ? "#381E72" : "#FFFFFF";
        var container = IsDark ? "#4F378B" : "#EADDFF";
        var onContainer = IsDark ? "#EADDFF" : "#21005D";
        var secondary = IsDark ? "#80CBC4" : "#006B60";
        theme.PrimaryMid = new(C(primary), C(onPrimary));
        theme.PrimaryLight = new(C(container), C(onContainer));
        theme.PrimaryDark = theme.PrimaryMid;
        theme.SecondaryMid = new(C(secondary), C(IsDark ? "#003731" : "#FFFFFF"));
        theme.SecondaryLight = theme.SecondaryMid; theme.SecondaryDark = theme.SecondaryMid;
        palette.SetTheme(theme);
        var resources = System.Windows.Application.Current.Resources;
        void Brush(string key, string color) => resources[key] = new SolidColorBrush(C(color));
        Brush("BrushPrimaryBg", IsDark ? "#141218" : "#FFFBFE");
        Brush("BrushSecondaryBg", IsDark ? "#1D1B20" : "#F7F2FA");
        Brush("BrushPanelBg", IsDark ? "#211F26" : "#F3EDF7");
        Brush("BrushPrimaryText", IsDark ? "#E6E1E5" : "#1C1B1F");
        Brush("BrushSecondaryText", IsDark ? "#CAC4D0" : "#49454F");
        Brush("BrushDisabledText", "#938F99"); Brush("BrushDivider", IsDark ? "#49454F" : "#CAC4D0");
        Brush("BrushAccentBlue", primary); Brush("BrushAccentBlueLight", container);
        Brush("BrushOnPrimary", onPrimary); Brush("BrushOnPrimaryContainer", onContainer);
        Brush("BrushAccentOrange", secondary); Brush("BrushAccentOrangeLight", IsDark ? "#005047" : "#9EF2E3");
        Brush("BrushOnSecondaryContainer", IsDark ? "#9EF2E3" : "#00201C");
        Brush("BrushStatusPending", IsDark ? "#FFB74D" : "#B85C16");
        Brush("BrushStatusOrganized", IsDark ? "#81C784" : "#237D4E");
        Brush("BrushDanger", IsDark ? "#F2B8B5" : "#B3261E");
        resources["MaterialDesign.Brush.Background"] = resources["BrushPrimaryBg"];
        resources["MaterialDesign.Brush.Foreground"] = resources["BrushPrimaryText"];
        resources["MaterialDesign.Brush.ForegroundLight"] = resources["BrushSecondaryText"];
        resources["MaterialDesign.Brush.Card.Background"] = resources["BrushPanelBg"];
        if (save)
        {
            var config = AppConfig.LoadOrDefault(AppPaths.SettingsPath);
            config.Appearance.ThemeMode = mode; config.Save(AppPaths.SettingsPath);
        }
        Changed?.Invoke();
    }
}
