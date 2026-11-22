using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ProjectXLR.ViewModels;

namespace ProjectXLR.Views;

public partial class LightingPage : UserControl
{
    private MainViewModel Vm => (MainViewModel)DataContext;

    public LightingPage() => InitializeComponent();

    private static string? TagOf(object sender) => (sender as FrameworkElement)?.Tag?.ToString();

    private void AccentColor_Click(object sender, RoutedEventArgs e)
    {
        if (TagOf(sender) is { } hex) Vm.ApplyColorToSelectedFader(hex);
    }

    private void ApplyLightingTheme_Click(object sender, RoutedEventArgs e)
    {
        if (TagOf(sender) is { } theme) Vm.ApplyLightingTheme(theme);
    }

    private void LightingEffect_Click(object sender, RoutedEventArgs e)
    {
        if (TagOf(sender) is { } effect) Vm.ApplyLightingEffect(effect);
    }

    private void SelectFaderLighting_Click(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(TagOf(sender), out int index))
        {
            Vm.SelectFaderLighting(index);
            // Picking a slider on the preview means "I want to color this one".
            Vm.IsLightingCustomTab = true;
        }
    }

    private void ApplyCustomHex_Click(object sender, RoutedEventArgs e) => Vm.ApplyCustomHexInput();
}

/// <summary>Maps the effect names the view model stores to the text it shows in ActiveLightingSummary.</summary>
internal static class LightingNames
{
    // Effect name, summary prefix, friendly name
    public static readonly (string Effect, string Prefix, string Friendly)[] All =
    [
        ("Solid", "Solid", "One solid color"),
        ("Custom", "Custom", "Your own colors"),
        ("Cyberpunk", "Cyberpunk", "Neon gradient"),
        ("Sunset", "Sunset", "Sunset gradient"),
        ("Ocean", "Ocean", "Ocean gradient"),
        ("FireIce", "Fire", "Fire and ice gradient"),
        ("Matrix", "Matrix", "Emerald gradient"),
        ("Monochrome", "Monochrome", "Silver gradient"),
        ("VoiceReactive", "Voice-Reactive", "Voice meter animation"),
        ("SpectrumSweep", "SpectrumSweep", "Rainbow sweep animation"),
        ("Breathing", "Breathing", "Breathing animation"),
        ("RetroRainbow", "Retro Rainbow", "Retro rainbow animation"),
        ("RainbowBright", "Rainbow Bright", "Rainbow animation"),
        ("RainbowDark", "Rainbow Dark", "Dark rainbow animation"),
        ("WaterfallDown", "Waterfall Down", "Waterfall down animation"),
        ("WaterfallUp", "Waterfall Up", "Waterfall up animation"),
    ];
}

/// <summary>True when ActiveLightingSummary describes the effect named in the converter parameter.</summary>
public sealed class EffectMatchConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string summary || parameter is not string effect) return false;
        foreach (var (name, prefix, _) in LightingNames.All)
        {
            if (name == effect) return summary.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Turns ActiveLightingSummary into a short plain-language name.</summary>
public sealed class LightingFriendlyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string summary || summary.Length == 0) return "Nothing yet";
        foreach (var (_, prefix, friendly) in LightingNames.All)
        {
            if (summary.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return friendly;
        }
        return summary;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Visible when a brush's color equals the hex code in the second value.</summary>
public sealed class SwatchMatchConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not SolidColorBrush brush || values[1] is not string hex)
            return Visibility.Collapsed;
        try
        {
            var color = (Color)ColorConverter.ConvertFromString("#" + hex.TrimStart('#'));
            return brush.Color.R == color.R && brush.Color.G == color.G && brush.Color.B == color.B
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        catch (FormatException)
        {
            return Visibility.Collapsed;
        }
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
