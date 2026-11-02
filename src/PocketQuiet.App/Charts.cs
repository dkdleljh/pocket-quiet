using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PocketQuiet.Core;

namespace PocketQuiet.App;

static class Charts
{
    static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(155, 176, 174));
    static readonly Pen Grid = new(new SolidColorBrush(Color.FromRgb(53, 78, 82)), 1);
    static readonly Pen Line = new(new SolidColorBrush(Color.FromRgb(142, 227, 176)), 2);
    static void Label(DrawingContext dc, string text, double x, double y) => dc.DrawText(
        new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Malgun Gothic"), 10, Muted, 1), new(x, y));
    static BitmapSource Render(double width, int height, Action<DrawingContext, double> paint)
    {
        int pixels = Math.Max(200, (int)width);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) { paint(dc, pixels); }
        var bitmap = new RenderTargetBitmap(pixels, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }
    public static BitmapSource History(double width, IReadOnlyList<(DateTimeOffset Time, double? Temperature)> samples, DateTimeOffset now) => Render(width, 140, (dc, w) =>
    {
        double left = 38, right = w - 10, top = 7, bottom = 115;
        double Y(double t) => bottom - Math.Clamp(t, 0, 115) / 115 * (bottom - top);
        foreach (int mark in new[] { 40, 60, 85, 110 }) { dc.DrawLine(Grid, new(left, Y(mark)), new(right, Y(mark))); Label(dc, $"{mark}°", 0, Y(mark) - 7); }
        Label(dc, "2분 전", left, 122); Label(dc, "1분 전", (left + right) / 2 - 16, 122); Label(dc, "현재", right - 25, 122);
        Point? previous = null; DateTimeOffset? previousTime = null;
        foreach (var sample in samples)
        {
            if (sample.Temperature is not double t) { previous = null; previousTime = null; continue; }
            double age = (now - sample.Time).TotalSeconds;
            if (age is < -1 or > 120) continue;
            var point = new Point(left + Math.Clamp(1 - age / 120, 0, 1) * (right - left), Y(t));
            if (previous is Point p && (sample.Time - previousTime!.Value).TotalSeconds <= 2.5) dc.DrawLine(Line, p, point);
            else dc.DrawEllipse(Line.Brush, null, point, 2, 2);
            previous = point; previousTime = sample.Time;
        }
    });
    public static BitmapSource Curve(double width, CurvePoint[] curve) => Render(width, 110, (dc, w) =>
    {
        double left = 38, right = w - 12, top = 8, bottom = 85;
        double X(double t) => left + (t - 20) / 65 * (right - left);
        double Y(double output) => bottom - (output - 30) / 70 * (bottom - top);
        foreach (int mark in new[] { 30, 65, 100 }) { dc.DrawLine(Grid, new(left, Y(mark)), new(right, Y(mark))); Label(dc, $"{mark}%", 0, Y(mark) - 6); }
        foreach (int temperature in new[] { 20, 40, 60, 85 }) Label(dc, $"{temperature}°C", X(temperature) - 14, 92);
        Point previous = new(X(20), Y(curve[0].Output));
        foreach (var p in curve)
        {
            Point current = new(X(p.Temperature), Y(p.Output)); dc.DrawLine(Line, previous, current);
            dc.DrawEllipse(Line.Brush, null, current, 3, 3); previous = current;
        }
    });
}
