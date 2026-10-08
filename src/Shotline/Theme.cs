using Microsoft.Win32;

namespace Shotline;

internal static class Theme
{
    internal const double CardWidth = 156, CardHeight = 166, CardSpacing = 178,
        LineHeight = 226, RopeTop = 12, RevealDelayMs = 250, RetractDelayMs = 650,
        HoldMs = 500, PeekMs = 2800, MotionMs = 260, DropMs = 420;
    internal static bool Motion { get; private set; }
    internal static void Apply(AppConfig config)
    {
        var dark = config.Theme == ColorTheme.Dark;
        if (config.Theme == ColorTheme.System)
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            dark = key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        Motion = !config.ReduceMotion && SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast;
        var resources = Application.Current.Resources;
        Set("Surface", dark ? "#252D2C" : "#FFFBF1");
        Set("Background", dark ? "#171E1D" : "#F2EFE5");
        Set("Ink", dark ? "#F3F0E6" : "#233B37");
        Set("Muted", dark ? "#ADBDB5" : "#687D74");
        Set("Edge", dark ? "#4C6058" : "#CCD3C6");
        Set("Accent", dark ? "#7CD4BA" : "#187B64");
        Set("Rope", dark ? "#BBC6BE" : "#738B7C");
        Set("Error", dark ? "#FFBBAA" : "#A93322");
        if (SystemParameters.HighContrast)
        {
            resources["Surface"] = SystemColors.WindowBrush;
            resources["Background"] = SystemColors.WindowBrush;
            resources["Ink"] = SystemColors.WindowTextBrush;
            resources["Muted"] = SystemColors.WindowTextBrush;
            resources["Accent"] = SystemColors.HighlightBrush;
            resources["Edge"] = SystemColors.WindowTextBrush;
        }
        void Set(string name, string color) => resources[name] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    }
    internal static double RopeY(double x, double width) => RopeTop +
        4 * Math.Min(26, width * 0.018) * (x / width) * (1 - x / width);
    internal static Brush Get(string name) => (Brush)Application.Current.Resources[name];
}
