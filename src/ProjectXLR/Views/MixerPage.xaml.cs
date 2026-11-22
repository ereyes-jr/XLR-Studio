using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ProjectXLR.Hardware;
using ProjectXLR.ViewModels;

namespace ProjectXLR.Views;

public partial class MixerPage : UserControl
{
    // Channels shown by default. Everything else sits behind "Show all channels".
    private static readonly ChannelName[] CoreChannels =
        [ChannelName.Mic, ChannelName.Game, ChannelName.Chat, ChannelName.Headphones];

    private MainViewModel? Vm => DataContext as MainViewModel;

    public MixerPage()
    {
        InitializeComponent();
        Loaded += (_, _) => BindChannelLists();
        DataContextChanged += (_, _) => BindChannelLists();
    }

    // Both lists read the same collection and are only filtered by channel type, never by
    // enabled state, so turning a channel off can never make it disappear from the list.
    private void BindChannelLists()
    {
        if (Vm is not { } vm) return;

        var core = new ListCollectionView(vm.AllChannels) { Filter = o => o is ChannelMixerItemViewModel c && CoreChannels.Contains(c.Channel) };
        var more = new ListCollectionView(vm.AllChannels) { Filter = o => o is ChannelMixerItemViewModel c && !CoreChannels.Contains(c.Channel) };
        CoreChannelsList.ItemsSource = core;
        MoreChannelsList.ItemsSource = more;
    }

    private void FaderLayoutPreset_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string preset) Vm?.ApplyFaderLayoutPreset(preset);
    }

    private void SetAll100_Click(object sender, RoutedEventArgs e) => Vm?.SetAllLevelsTo100Percent();

    private void EnableFaderChannel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement el) return;
        if (el.Tag is FaderName fader) Vm?.EnableFaderChannel(fader);
        else if (el.DataContext is FaderSlotViewModel slot) Vm?.EnableFaderChannel(slot.Fader);
    }

    private void ResetToMinimalChannels_Click(object sender, RoutedEventArgs e) => Vm?.ResetToMinimalChannels();

    private void EnableStandardChannels_Click(object sender, RoutedEventArgs e) => Vm?.EnableStandardChannels();

    private void SyncWindowsEndpoints_Click(object sender, RoutedEventArgs e) => Vm?.SyncWindowsSoundEndpoints();

    private void OpenWindowsSoundControlPanel_Click(object sender, RoutedEventArgs e) => Vm?.OpenWindowsSoundControlPanel();
}

/// <summary>Removes the leading emoji from channel names such as "Mic Input" so the page stays plain text.</summary>
public sealed class PlainLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string text = value as string ?? string.Empty;
        int i = 0;
        while (i < text.Length && !char.IsLetterOrDigit(text[i])) i++;
        return text[i..];
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Turns the view model's "FADER 1" label into "Slider 1".</summary>
public sealed class SliderNumberConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string text = value as string ?? string.Empty;
        string digits = new(text.Where(char.IsDigit).ToArray());
        return digits.Length > 0 ? $"Slider {digits}" : "Slider";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>One row of four equal columns when there is room, otherwise two rows of two. Never scrolls sideways.</summary>
public sealed class FaderGrid : Panel
{
    private const double Gap = 8;
    private const double MinFourColumnCell = 150;

    private static int ColumnsFor(double width)
    {
        if (double.IsInfinity(width)) return 4;
        return width >= 4 * MinFourColumnCell + 3 * Gap ? 4 : 2;
    }

    protected override Size MeasureOverride(Size available)
    {
        int columns = ColumnsFor(available.Width);
        double cell = double.IsInfinity(available.Width) ? MinFourColumnCell : (available.Width - Gap * (columns - 1)) / columns;
        double total = 0, rowHeight = 0;
        int index = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(cell, double.PositiveInfinity));
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            if (++index % columns == 0) { total += rowHeight + Gap; rowHeight = 0; }
        }
        if (rowHeight > 0) total += rowHeight + Gap;
        return new Size(double.IsInfinity(available.Width) ? cell * columns + Gap * (columns - 1) : available.Width, Math.Max(0, total - Gap));
    }

    protected override Size ArrangeOverride(Size final)
    {
        int columns = ColumnsFor(final.Width);
        double cell = (final.Width - Gap * (columns - 1)) / columns;
        var children = InternalChildren.Cast<UIElement>().ToList();
        double y = 0;
        for (int start = 0; start < children.Count; start += columns)
        {
            var row = children.Skip(start).Take(columns).ToList();
            double rowHeight = row.Max(c => c.DesiredSize.Height);
            for (int i = 0; i < row.Count; i++)
                row[i].Arrange(new Rect(i * (cell + Gap), y, cell, rowHeight));
            y += rowHeight + Gap;
        }
        return final;
    }
}
