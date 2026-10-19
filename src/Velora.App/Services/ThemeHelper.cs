// SPDX-License-Identifier: GPL-3.0-or-later
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Velora.App;

/// <summary>
/// Colores de la marca Velora sobre el tema Fluent de Windows 11: rojo carmesí <c>#C8102E</c> (color principal),
/// blanco suave <c>#F7F7F9</c> (superficies claras) y grafito <c>#454650</c> (texto e iconos).
/// Detecta además si Windows (o la opción --tema) usa el modo oscuro.
/// </summary>
internal static class ThemeHelper
{
    public static readonly Color Crimson = Color.FromRgb(0xC8, 0x10, 0x2E);
    public static readonly Color SoftWhite = Color.FromRgb(0xF7, 0xF7, 0xF9);
    public static readonly Color Graphite = Color.FromRgb(0x45, 0x46, 0x50);

    public static bool IsDark()
    {
        var mode = Application.Current.ThemeMode;
        if (mode == ThemeMode.Dark) return true;
        if (mode == ThemeMode.Light) return false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Sustituye el color de acento de Windows por el de la marca (y, en claro, las superficies y el texto). Se repite al cambiar de tema.</summary>
    public static void ApplyBrand()
    {
        var res = Application.Current.Resources;
        bool dark = IsDark();

        void Set(string key, Color c) => res[key] = Frozen(c);

        // Acento: el carmesí de la marca; más luminoso en oscuro para que el texto resalte
        Color accent = dark ? Color.FromRgb(0xD9, 0x25, 0x40) : Crimson;
        Set("AccentFillColorDefaultBrush", accent);
        Set("AccentFillColorSecondaryBrush", Mix(accent, dark ? Colors.White : Colors.Black, dark ? 0.10 : 0.10));
        Set("AccentFillColorTertiaryBrush", Mix(accent, dark ? Colors.White : Colors.Black, dark ? 0.18 : 0.20));
        Set("TextOnAccentFillColorPrimaryBrush", Colors.White);
        Set("AccentTextFillColorPrimaryBrush", dark ? Color.FromRgb(0xFF, 0x7A, 0x8B) : Color.FromRgb(0xA8, 0x0D, 0x27));
        Set("AccentTextFillColorSecondaryBrush", dark ? Color.FromRgb(0xFF, 0x9A, 0xA7) : Color.FromRgb(0x8A, 0x0A, 0x20));
        Set("AccentTextFillColorTertiaryBrush", dark ? Color.FromRgb(0xFF, 0x6A, 0x7D) : Crimson);
        Set("FocusStrokeColorOuterBrush", dark ? Colors.White : Graphite);

        // Los estilos de los controles copian el acento en sus propios recursos al cargar: se sustituyen uno a uno.
        Color hover = Mix(accent, dark ? Colors.White : Colors.Black, 0.10), pressed = Mix(accent, dark ? Colors.White : Colors.Black, 0.20);
        foreach (string b in new[] { "AccentButtonBackground", "ToggleButtonBackgroundChecked", "CheckBoxCheckBackgroundFillChecked", "CheckBoxCheckBackgroundStrokeChecked" })
        {
            Set(b, accent);
            Set(b + "PointerOver", hover);
            Set(b + "Pressed", pressed);
        }

        Set("CheckBoxCheckBackgroundFillIndeterminate", accent);
        Set("CheckBoxCheckBackgroundStrokeIndeterminate", accent);
        Set("ComboBoxBorderBrushFocused", accent);
        Set("ProgressBarForeground", accent);
        Set("TreeViewItemSelectionIndicatorForeground", accent);
        Set("HyperlinkForeground", dark ? Color.FromRgb(0xFF, 0x7A, 0x8B) : Color.FromRgb(0xA8, 0x0D, 0x27));
        Set("HyperlinkForegroundPointerOver", dark ? Color.FromRgb(0xFF, 0x9A, 0xA7) : Color.FromRgb(0x8A, 0x0A, 0x20));
        foreach (string c in new[] { "ToggleButtonForegroundChecked", "ToggleButtonForegroundCheckedPointerOver", "ToggleButtonForegroundCheckedPressed", "AccentButtonForeground",
                     "AccentButtonForegroundPointerOver", "AccentButtonForegroundPressed", "CheckBoxCheckGlyphForegroundChecked" })
            Set(c, Colors.White);

        // Selección de listas y de texto en cuadros: tinte carmesí
        var tint = dark ? Color.FromArgb(120, 0xFF, 0x5A, 0x70) : Color.FromArgb(70, 0xC8, 0x10, 0x2E);
        var onTint = dark ? Colors.White : Graphite;
        res["BrandSelectionBrush"] = Frozen(tint);
        res[SystemColors.HighlightBrushKey] = Frozen(tint);
        res[SystemColors.InactiveSelectionHighlightBrushKey] = Frozen(tint);
        res[SystemColors.HighlightTextBrushKey] = Frozen(onTint);
        res[SystemColors.InactiveSelectionHighlightTextBrushKey] = Frozen(onTint);

        if (!dark)
        {
            // Superficies claras en blanco suave y texto en grafito
            Set("SolidBackgroundFillColorBaseBrush", SoftWhite);
            Set("SolidBackgroundFillColorSecondaryBrush", Color.FromRgb(0xF0, 0xF0, 0xF3));
            Set("SolidBackgroundFillColorTertiaryBrush", Color.FromRgb(0xFC, 0xFC, 0xFD));
            Set("TextFillColorPrimaryBrush", Graphite);
            Set("TextFillColorSecondaryBrush", Color.FromRgb(0x6B, 0x6C, 0x78));
            Set("TextFillColorTertiaryBrush", Color.FromRgb(0x8A, 0x8B, 0x96));
        }
        else
        {
            foreach (string k in new[] { "SolidBackgroundFillColorBaseBrush", "SolidBackgroundFillColorSecondaryBrush", "SolidBackgroundFillColorTertiaryBrush",
                         "TextFillColorPrimaryBrush", "TextFillColorSecondaryBrush", "TextFillColorTertiaryBrush" })
                res.Remove(k); // en oscuro valen los del tema de Windows
        }
    }

    private static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    public static Brush ViewerBackdrop() => Frozen(IsDark() ? Color.FromRgb(0x26, 0x26, 0x2A) : Color.FromRgb(0xE3, 0xE3, 0xE8));

    public static Brush SelectionBrush() => Frozen(IsDark() ? Color.FromArgb(120, 0xFF, 0x5A, 0x70) : Color.FromArgb(80, 0xC8, 0x10, 0x2E));

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
