using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace ProjectXLR.Controls;

/// <summary>
/// Lays children out in as many equal columns as fit, always filling the shortest column.
/// This is what makes a page reveal more side by side when the window grows and stack when it shrinks.
/// </summary>
public sealed class AdaptiveColumns : Panel
{
    public static readonly DependencyProperty MinColumnWidthProperty = DependencyProperty.Register(
        nameof(MinColumnWidth), typeof(double), typeof(AdaptiveColumns),
        new FrameworkPropertyMetadata(420.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(AdaptiveColumns),
        new FrameworkPropertyMetadata(20.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double MinColumnWidth
    {
        get => (double)GetValue(MinColumnWidthProperty);
        set => SetValue(MinColumnWidthProperty, value);
    }

    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    private (int Columns, double ColumnWidth) ColumnsFor(double width)
    {
        if (double.IsInfinity(width) || width <= 0) return (1, MinColumnWidth);
        int columns = Math.Max(1, (int)Math.Floor((width + Gap) / (MinColumnWidth + Gap)));
        return (columns, (width - Gap * (columns - 1)) / columns);
    }

    protected override Size MeasureOverride(Size available)
    {
        var (columns, columnWidth) = ColumnsFor(available.Width);
        var heights = new double[columns];
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(columnWidth, double.PositiveInfinity));
            if (child.Visibility == Visibility.Collapsed) continue;
            heights[ShortestIndex(heights)] += child.DesiredSize.Height + Gap;
        }
        double width = double.IsInfinity(available.Width) ? columnWidth : available.Width;
        return new Size(width, Math.Max(0, Max(heights) - Gap));
    }

    protected override Size ArrangeOverride(Size final)
    {
        var (columns, columnWidth) = ColumnsFor(final.Width);
        var heights = new double[columns];
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                child.Arrange(new Rect());
                continue;
            }
            int i = ShortestIndex(heights);
            child.Arrange(new Rect(i * (columnWidth + Gap), heights[i], columnWidth, child.DesiredSize.Height));
            heights[i] += child.DesiredSize.Height + Gap;
        }
        return final;
    }

    private static int ShortestIndex(double[] values)
    {
        int best = 0;
        for (int i = 1; i < values.Length; i++)
        {
            if (values[i] < values[best] - 0.5) best = i;
        }
        return best;
    }

    private static double Max(double[] values)
    {
        double max = 0;
        foreach (double v in values) max = Math.Max(max, v);
        return max;
    }
}

/// <summary>
/// A settings row with two children, label then control. They sit side by side when both fit,
/// and the control drops under the label when they don't, so text can never overlap a control.
/// </summary>
public sealed class AdaptiveRow : Panel
{
    private const double Spacing = 16;
    private const double StackedSpacing = 9;
    private const double MinLabelWidth = 130;

    private bool Fits(double width) =>
        InternalChildren.Count < 2 ||
        InternalChildren[1].DesiredSize.Width + Spacing + MinLabelWidth <= width;

    protected override Size MeasureOverride(Size available)
    {
        if (InternalChildren.Count == 0) return new Size();

        var label = InternalChildren[0];
        if (InternalChildren.Count == 1)
        {
            label.Measure(available);
            return label.DesiredSize;
        }

        var control = InternalChildren[1];
        control.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        if (control.DesiredSize.Width > available.Width)
        {
            control.Measure(new Size(available.Width, double.PositiveInfinity));
        }

        double width = double.IsInfinity(available.Width) ? 600 : available.Width;
        if (Fits(width))
        {
            label.Measure(new Size(Math.Max(0, width - control.DesiredSize.Width - Spacing), double.PositiveInfinity));
            return new Size(width, Math.Max(label.DesiredSize.Height, control.DesiredSize.Height));
        }

        label.Measure(new Size(width, double.PositiveInfinity));
        return new Size(width, label.DesiredSize.Height + StackedSpacing + control.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        if (InternalChildren.Count == 0) return final;

        var label = InternalChildren[0];
        if (InternalChildren.Count == 1)
        {
            label.Arrange(new Rect(final));
            return final;
        }

        var control = InternalChildren[1];
        if (Fits(final.Width))
        {
            double controlWidth = control.DesiredSize.Width;
            label.Arrange(new Rect(0, (final.Height - label.DesiredSize.Height) / 2, Math.Max(0, final.Width - controlWidth - Spacing), label.DesiredSize.Height));
            control.Arrange(new Rect(final.Width - controlWidth, (final.Height - control.DesiredSize.Height) / 2, controlWidth, control.DesiredSize.Height));
        }
        else
        {
            label.Arrange(new Rect(0, 0, final.Width, label.DesiredSize.Height));
            control.Arrange(new Rect(0, label.DesiredSize.Height + StackedSpacing, final.Width, control.DesiredSize.Height));
        }
        return final;
    }
}

/// <summary>Lightbulb help: shows its text on hover and pins it open on click.</summary>
public sealed class InfoTip : Button
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(InfoTip), new PropertyMetadata(string.Empty, OnTextChanged));

    private readonly Popup _popup;
    private readonly TextBlock _popupText;

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public InfoTip()
    {
        Focusable = false;
        _popupText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12.5,
            LineHeight = 18,
            Foreground = new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF7))
        };
        _popup = new Popup
        {
            PlacementTarget = this,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
            Child = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2F)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x43)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(12, 9, 12, 9),
                MaxWidth = 340,
                Child = _popupText
            }
        };
        Click += (_, _) => _popup.IsOpen = !_popup.IsOpen;
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var tip = (InfoTip)d;
        tip.ToolTip = e.NewValue;
        tip._popupText.Text = e.NewValue as string ?? string.Empty;
    }
}

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
