namespace Shotline;

internal sealed class HintWindow : Window
{
    private readonly TextBlock text = new() { FontSize = 12, Margin = new Thickness(4, 0, 4, 5) };
    internal bool HasMessage { get; private set; }
    private readonly AppController controller;
    internal HintWindow(AppController controller)
    {
        this.controller = controller;
        Width = 360; Height = 112; WindowStyle = WindowStyle.None; AllowsTransparency = true;
        Background = Brushes.Transparent; ShowActivated = false; ShowInTaskbar = false; Topmost = true;
        ResizeMode = ResizeMode.NoResize; Focusable = false; Title = "晾图提示";
        var border = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10, 10, 8),
            Margin = new Thickness(5), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Top };
        border.SetResourceReference(Border.BackgroundProperty, "Surface");
        border.SetResourceReference(Border.BorderBrushProperty, "Edge");
        var stack = new StackPanel(); stack.Children.Add(text);
        var button = new Button { Content = "设置与状态 ↗", FontSize = 11, Padding = new Thickness(8, 3, 8, 3),
            HorizontalAlignment = HorizontalAlignment.Left, MinHeight = 26, Focusable = false };
        button.Click += (_, _) => controller.ShowSettings(); stack.Children.Add(button); border.Child = stack; Content = border;
        AllowDrop = true;
        DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        Drop += (_, e) => { if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) controller.AddFiles(paths); e.Handled = true; };
        SourceInitialized += (_, _) => Native.Configure(this, false); ClearMessage();
    }
    internal void Place(Native.Monitor monitor, double centre)
    { Native.Place(this, monitor.Work.Left + (int)((centre - Width / 2) * monitor.Scale),
        monitor.Work.Top + (int)((Theme.RopeY(centre, monitor.Work.Width / monitor.Scale) + 12) * monitor.Scale), Width, Height, monitor.Scale); }
    internal void SetMessage(string message) { HasMessage = true; text.Text = message; }
    internal void ClearMessage() { HasMessage = false; text.Text = "截图后会挂在这里。也可以拖入 PNG、JPEG、BMP。"; }
    internal void ShowHint() { if (!IsVisible) Show(); }
}
