using System.Windows.Shapes;

namespace Shotline;

internal sealed class RopeWindow : Window
{
    private readonly Canvas canvas = new() { IsHitTestVisible = false };
    internal RopeWindow()
    {
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; ShowActivated = false; Topmost = true; ResizeMode = ResizeMode.NoResize;
        Focusable = false; Content = canvas; Title = "Shotline 绳线";
        SourceInitialized += (_, _) => Native.Configure(this, true);
    }
    internal void Place(Native.Monitor monitor)
    {
        Width = monitor.Work.Width / monitor.Scale; Height = Theme.LineHeight;
        Native.Place(this, monitor.Work.Left, monitor.Work.Top, Width, Height, monitor.Scale);
        canvas.Children.Clear();
        var figure = new PathFigure { StartPoint = new Point(-10, Theme.RopeTop), IsClosed = false };
        figure.Segments.Add(new QuadraticBezierSegment(new Point(Width / 2,
            Theme.RopeTop + 2 * Math.Min(26, Width * 0.018)), new Point(Width + 10, Theme.RopeTop), true));
        var path = new System.Windows.Shapes.Path { Data = new PathGeometry([figure]), StrokeThickness = 1.4 };
        path.SetResourceReference(Shape.StrokeProperty, "Rope"); canvas.Children.Add(path);
    }
    internal void Reveal(bool on)
    {
        if (on)
        {
            if (!IsVisible) Show();
            Opacity = 1;
            if (Theme.Motion) BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(Theme.MotionMs)));
        }
        else { BeginAnimation(OpacityProperty, null); Hide(); }
    }
}
