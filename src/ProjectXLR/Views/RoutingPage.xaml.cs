using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using ProjectXLR.Hardware;
using ProjectXLR.ViewModels;

namespace ProjectXLR.Views;

public partial class RoutingPage : UserControl
{
    private readonly Dictionary<BasicInputDevice, FrameworkElement> _inputSocketElements = new();
    private MainViewModel? _subscribedVm;
    private bool _redrawQueued;

    private MainViewModel Vm => (MainViewModel)DataContext;

    public RoutingPage()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => ScheduleRedraw();
        SizeChanged += (_, _) => ScheduleRedraw();
        IsVisibleChanged += (_, _) => ScheduleRedraw();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_subscribedVm != null)
        {
            _subscribedVm.RoutingGraphChanged -= OnGraphChanged;
            _subscribedVm.ActiveRoutingRows.CollectionChanged -= OnRowsChanged;
        }

        _subscribedVm = e.NewValue as MainViewModel;

        if (_subscribedVm != null)
        {
            _subscribedVm.RoutingGraphChanged += OnGraphChanged;
            _subscribedVm.ActiveRoutingRows.CollectionChanged += OnRowsChanged;
        }
        ScheduleRedraw();
    }

    private void OnGraphChanged(object? sender, EventArgs e) => ScheduleRedraw();

    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e) => ScheduleRedraw();

    private void Board_SizeChanged(object sender, SizeChangedEventArgs e) => ScheduleRedraw();

    private void Route_Click(object sender, RoutedEventArgs e) => ScheduleRedraw();

    private void ResetConnections_Click(object sender, RoutedEventArgs e) => Vm.ApplyRoutingPreset("ResetDefault");

    private void InputSocket_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.Tag is BasicInputDevice input)
        {
            _inputSocketElements[input] = elem;
            ScheduleRedraw();
        }
    }

    private void InputSocket_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.Tag is BasicInputDevice input &&
            _inputSocketElements.TryGetValue(input, out var current) && ReferenceEquals(current, elem))
        {
            _inputSocketElements.Remove(input);
        }
    }

    private void ScheduleRedraw()
    {
        if (_redrawQueued) return;
        _redrawQueued = true;
        Dispatcher.InvokeAsync(() =>
        {
            _redrawQueued = false;
            RedrawCables();
        }, DispatcherPriority.Loaded);
    }

    private void RedrawCables()
    {
        if (RoutingWireCanvas == null) return;
        RoutingWireCanvas.Children.Clear();
        if (_subscribedVm == null || !IsVisible) return;

        foreach (var row in _subscribedVm.ActiveRoutingRows)
        {
            if (!_inputSocketElements.TryGetValue(row.Input, out var inSocket) || !inSocket.IsVisible) continue;

            Point start;
            try
            {
                start = inSocket.TransformToVisual(RoutingWireCanvas).Transform(new Point(inSocket.ActualWidth / 2.0, inSocket.ActualHeight / 2.0));
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            Brush brush;
            try
            {
                brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(row.WireColorHex));
                brush.Freeze();
            }
            catch (FormatException)
            {
                brush = Brushes.Gray;
            }

            if (row.Headphones) DrawWire(start, SocketOutHeadphones, brush);
            if (row.BroadcastMix) DrawWire(start, SocketOutBroadcastMix, brush);
            if (row.ChatMic && row.CanRouteToChatMic) DrawWire(start, SocketOutChatMic, brush);
            if (row.LineOut) DrawWire(start, SocketOutLineOut, brush);
        }
    }

    private void DrawWire(Point start, FrameworkElement? outSocket, Brush brush)
    {
        if (outSocket == null || !outSocket.IsVisible) return;

        Point end;
        try
        {
            end = outSocket.TransformToVisual(RoutingWireCanvas).Transform(new Point(outSocket.ActualWidth / 2.0, outSocket.ActualHeight / 2.0));
        }
        catch (InvalidOperationException)
        {
            return;
        }

        double dx = Math.Max(24.0, Math.Abs(end.X - start.X) * 0.5);
        var figure = new PathFigure { StartPoint = start, IsClosed = false, IsFilled = false };
        figure.Segments.Add(new BezierSegment(new Point(start.X + dx, start.Y), new Point(end.X - dx, end.Y), end, true));
        var geometry = new PathGeometry([figure]);
        geometry.Freeze();

        RoutingWireCanvas.Children.Add(new Path
        {
            Data = geometry,
            Stroke = brush,
            StrokeThickness = 2.0,
            Opacity = 0.8,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        });
    }
}
