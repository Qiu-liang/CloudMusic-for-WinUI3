using System;

namespace music.Services
{
    public enum ThemeMode { System, Light, Dark }

    public static class ThemeSettings
    {
        public static ThemeMode Parse(string? value) => value switch
        {
            "Light" => ThemeMode.Light,
            "Dark" => ThemeMode.Dark,
            _ => ThemeMode.System
        };

        public static string Serialize(ThemeMode theme) => theme.ToString();

        public static bool IsDark(ThemeMode theme, bool systemIsDark) => theme switch
        {
            ThemeMode.Dark => true,
            ThemeMode.Light => false,
            _ => systemIsDark
        };
    }
}
