using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ProjectXLR.ViewModels;

namespace ProjectXLR;

/// <summary>
/// Developer diagnostic (run with --screenshots): renders every page at three window sizes to PNG and
/// audits visible text for overlaps and clipping, without connecting to the GoXLR or saving anything.
/// Output: &lt;bin&gt;/ui_audit/*.png and ui_audit/report.txt
/// </summary>
internal static class UiScreenshotTool
{
    public static async void Run(Application app)
    {
        MainViewModel.PreviewMode = true;
        string outDir = Path.Combine(AppContext.BaseDirectory, "ui_audit");
        Directory.CreateDirectory(outDir);
        var report = new StringBuilder();
        try
        {
            var win = new MainWindow
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 20,
                Top = 20,
                ShowActivated = false
            };
            win.Show();
            var vm = (MainViewModel)win.DataContext;

            var pages = new List<(string Name, Action Select)>
            {
                ("voice", () => vm.IsVoiceTab = true),
                ("mixer", () => vm.IsMixerTab = true),
                ("routing", () => vm.IsRoutingTab = true),
                ("lighting", () => vm.IsLightingTab = true),
                ("settings", () => vm.IsSettingsTab = true),
            };
            var sizes = new List<(string Name, double Width, double Height)>
            {
                ("default", 1240, 860),
                ("narrow", 760, 620),
                ("wide", 1800, 1040),
            };

            foreach (var (sizeName, width, height) in sizes)
            {
                win.Width = width;
                win.Height = height;
                foreach (var (name, select) in pages)
                {
                    select();
                    await Settle();
                    SaveVisual(win, Path.Combine(outDir, $"{name}_{sizeName}.png"));
                    AuditText($"{name} @ {sizeName}", (FrameworkElement)win.Content, report);
                }
            }
            win.Width = 1240;
            win.Height = 860;
            vm.IsVoiceTab = true;
            foreach (var stage in new[] { VoiceCheckStage.Intro, VoiceCheckStage.Keys, VoiceCheckStage.Quiet })
            {
                vm.ShowVoiceCheckStagePreview(stage);
                await Settle();
                SaveVisual(win, Path.Combine(outDir, $"voicecheck_{stage}.png"));
                AuditText($"voice check {stage}", (FrameworkElement)win.Content, report);
            }
            vm.ShowVoiceCheckPreview();
            await Settle();
            SaveVisual(win, Path.Combine(outDir, "voicecheck_result.png"));
            vm.CloseVoiceCheck();
            vm.ShowCoachPreview();
            await Settle();
            SaveVisual(win, Path.Combine(outDir, "coach.png"));
            win.FullExit();
        }
        catch (Exception ex)
        {
            report.AppendLine("ERROR: " + ex);
        }
        File.WriteAllText(Path.Combine(outDir, "report.txt"), report.ToString());
        app.Shutdown(0);
    }

    private static async Task Settle()
    {
        for (int i = 0; i < 4; i++)
        {
            await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await Task.Delay(120);
        }
    }

    private static void SaveVisual(FrameworkElement element, string path)
    {
        double w = Math.Max(1, element.ActualWidth);
        double h = Math.Max(1, element.ActualHeight);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x16)), null, new Rect(0, 0, w, h));
            dc.DrawRectangle(new VisualBrush(element) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                null, new Rect(0, 0, w, h));
        }
        var rtb = new RenderTargetBitmap((int)Math.Ceiling(w), (int)Math.Ceiling(h), 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            yield return c;
            foreach (var d in Descendants(c)) yield return d;
        }
    }

    private static void AuditText(string page, FrameworkElement root, StringBuilder report)
    {
        var items = new List<(TextBlock Tb, Rect Rect, string Text)>();
        foreach (var tb in Descendants(root).OfType<TextBlock>())
        {
            if (!tb.IsVisible || tb.ActualWidth < 1 || tb.ActualHeight < 1) continue;
            string text = tb.Text;
            if (string.IsNullOrWhiteSpace(text)) continue;
            Rect r;
            try { r = tb.TransformToAncestor(root).TransformBounds(new Rect(0, 0, tb.ActualWidth, tb.ActualHeight)); }
            catch { continue; }

            if (tb.TextWrapping == TextWrapping.NoWrap)
            {
                var ft = new FormattedText(text, CultureInfo.CurrentUICulture, tb.FlowDirection,
                    new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch), tb.FontSize, Brushes.White,
                    VisualTreeHelper.GetDpi(tb).PixelsPerDip);
                double needed = ft.WidthIncludingTrailingWhitespace + tb.Padding.Left + tb.Padding.Right;
                if (needed > tb.ActualWidth + 1.5)
                {
                    report.AppendLine($"[{page}] CLIPPED ({needed:F0}px needed, {tb.ActualWidth:F0}px given): \"{Trim(text)}\"");
                }
            }
            items.Add((tb, r, text));
        }

        for (int i = 0; i < items.Count; i++)
        {
            for (int j = i + 1; j < items.Count; j++)
            {
                var a = items[i]; var b = items[j];
                var inter = Rect.Intersect(a.Rect, b.Rect);
                if (inter.IsEmpty || inter.Width < 2 || inter.Height < 2) continue;
                report.AppendLine($"[{page}] OVERLAP: \"{Trim(a.Text)}\" <-> \"{Trim(b.Text)}\"");
            }
        }
    }

    private static string Trim(string s) => s.Length > 60 ? s[..57] + "..." : s;
}
