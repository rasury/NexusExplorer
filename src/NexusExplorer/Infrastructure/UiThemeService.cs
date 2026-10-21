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
        var primary = IsDark ? "#A2D3E7" : "#3E7B97";
        var onPrimary = IsDark ? "#173D4D" : "#FFFFFF";
        var container = IsDark ? "#335766" : "#D5E8EF";
        var onContainer = IsDark ? "#D5E8EF" : "#294958";
        var secondary = IsDark ? "#80CBC4" : "#006B60";
        theme.PrimaryMid = new(C(primary), C(onPrimary));
        theme.PrimaryLight = new(C(container), C(onContainer));
        theme.PrimaryDark = theme.PrimaryMid;
        theme.SecondaryMid = new(C(secondary), C(IsDark ? "#003731" : "#FFFFFF"));
        theme.SecondaryLight = theme.SecondaryMid; theme.SecondaryDark = theme.SecondaryMid;
        palette.SetTheme(theme);
        var resources = System.Windows.Application.Current.Resources;
        void Brush(string key, string color) => resources[key] = new SolidColorBrush(C(color));
        Brush("BrushPrimaryBg", IsDark ? "#1D3442" : "#E4EEF0");
        Brush("BrushSecondaryBg", IsDark ? "#243E49" : "#EAF3F5");
        Brush("BrushPanelBg", IsDark ? "#21333E" : "#F6FAFB");
        Brush("BrushPrimaryText", IsDark ? "#E0EDF1" : "#294958");
        Brush("BrushSecondaryText", IsDark ? "#A3BBC5" : "#526F7D");
        Brush("BrushDisabledText", "#938F99"); Brush("BrushDivider", IsDark ? "#447D9EAD" : "#4490ADB8");
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
        Brush("BrushPrismRim", IsDark ? "#55C4EAF3" : "#B3FFFFFF");
        Brush("BrushWallpaperShade", IsDark ? "#800C203A" : "#00000000");
        Brush("BrushPrismDialog", IsDark ? "#EE294550" : "#E6E4F0F3");
        resources["BrushPrismSurface"] = new LinearGradientBrush(new GradientStopCollection
        {
            new(C(IsDark ? "#88294550" : "#80FFFFFF"), 0),
            new(C(IsDark ? "#66203540" : "#26FFFFFF"), .45),
            new(C(IsDark ? "#77313F49" : "#38E8FAFF"), 1)
        }, new Point(0, 0), new Point(1, 1));
        if (save)
        {
            var config = AppConfig.LoadOrDefault(AppPaths.SettingsPath);
            config.Appearance.ThemeMode = mode; config.Save(AppPaths.SettingsPath);
        }
        Changed?.Invoke();
    }
}
