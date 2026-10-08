namespace Shotline;

internal partial class CardWindow : Window
{
    private readonly AppController controller;
    internal CardViewModel Model { get; }
    private readonly DispatcherTimer hold = new(), click = new(), feedbackTimer = new();
    private Point? down;
    private bool consumed, falling, copying, dragging, menuOpen, closed;
    private Native.Monitor? monitor;
    private int screenX, screenY;
    internal CardWindow(AppController controller, CardViewModel model)
    {
        InitializeComponent(); this.controller = controller; Model = model; DataContext = model;
        Title = "晾图 · " + model.FileName; Tilt.Angle = model.Tilt;
        SourceInitialized += (_, _) => Native.Configure(this, false);
        hold.Interval = TimeSpan.FromMilliseconds(Theme.HoldMs);
        hold.Tick += (_, _) =>
        {
            hold.Stop(); consumed = true; EndPress(); controller.Edit(Model); Nudge();
        };
        click.Interval = TimeSpan.FromMilliseconds(Native.GetDoubleClickTime() + 15);
        click.Tick += async (_, _) =>
        {
            click.Stop(); copying = true; UpdateInteraction();
            try { if (!falling && await controller.CopyAsync(Model)) FeedbackSuccess(); }
            finally { copying = false; UpdateInteraction(); }
        };
        feedbackTimer.Interval = TimeSpan.FromMilliseconds(1100);
        feedbackTimer.Tick += (_, _) => { feedbackTimer.Stop(); Feedback.Visibility = Visibility.Collapsed; };
        Card.MouseEnter += (_, _) => { RemoveButton.Opacity = 1; };
        Card.MouseLeave += (_, _) => { RemoveButton.Opacity = 0; };
        Card.PreviewMouseLeftButtonDown += Down;
        Card.PreviewMouseLeftButtonUp += Up;
        Card.PreviewMouseMove += Move;
        Card.LostMouseCapture += (_, _) => { if (down is not null) EndPress(); };
        RemoveButton.Click += (_, _) => { click.Stop(); controller.Remove(Model); };
        Card.ContextMenu = Menu();
        Card.ContextMenuOpening += (_, _) =>
        {
            click.Stop(); EndPress(); menuOpen = true; UpdateInteraction();
            if (Card.ContextMenu.Items.OfType<MenuItem>().LastOrDefault() is MenuItem recycle)
                recycle.Header = Model.Item.Managed ? "将管理副本移到回收站…" : "将外部源文件移到回收站…";
        };
        Card.ContextMenuClosing += (_, _) => { menuOpen = false; UpdateInteraction(); };
        Closed += (_, _) => { closed = true; hold.Stop(); click.Stop(); feedbackTimer.Stop(); UpdateInteraction(); };
    }
    private bool OnCloseButton(MouseEventArgs e)
    {
        var element = e.OriginalSource as DependencyObject;
        while (element is not null)
        {
            if (element == RemoveButton) return true;
            element = element is FrameworkContentElement content ? content.Parent : VisualTreeHelper.GetParent(element);
        }
        return false;
    }
    private void Down(object sender, MouseButtonEventArgs e)
    {
        if (OnCloseButton(e) || falling) return;
        e.Handled = true; consumed = false; click.Stop();
        if (e.ClickCount >= 2)
        { click.Stop(); consumed = true; EndPress(); controller.Open(Model); return; }
        down = e.GetPosition(Card); Card.CaptureMouse(); hold.Start();
        UpdateInteraction();
    }
    private void Up(object sender, MouseButtonEventArgs e)
    {
        if (OnCloseButton(e)) return;
        var clicked = down is not null && !consumed;
        EndPress(); e.Handled = true;
        if (clicked) { click.Stop(); click.Start(); UpdateInteraction(); }
    }
    private async void Move(object sender, MouseEventArgs e)
    {
        if (down is not Point start || e.LeftButton != MouseButtonState.Pressed || consumed) return;
        var current = e.GetPosition(Card);
        if (Math.Abs(current.X - start.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        consumed = true; click.Stop(); EndPress();
        dragging = true; UpdateInteraction();
        Card.Opacity = 0.55;
        try
        {
            var data = new DataObject(); data.SetData(DataFormats.FileDrop, new[] { Model.Item.Path });
            data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(1u)));
            var effect = DragDrop.DoDragDrop(Card, data, DragDropEffects.Copy | DragDropEffects.Move);
            var outcome = (effect & DragDropEffects.Move) != 0 ? DropOutcome.Move :
                (effect & DragDropEffects.Copy) != 0 ? DropOutcome.Copy : DropOutcome.Cancelled;
            await controller.CompleteDragAsync(Model, outcome);
            if (outcome == DropOutcome.Cancelled) Nudge();
        }
        catch (Exception ex) when (ex is ExternalException or InvalidOperationException or IOException)
        { controller.Diagnostics.Fail("drag.failed", "拖放失败，卡片保留。", ex, Model.Id); Nudge(); }
        finally { Card.Opacity = 1; dragging = false; UpdateInteraction(); }
    }
    private void EndPress()
    { hold.Stop(); down = null; if (Card.IsMouseCaptured) Card.ReleaseMouseCapture(); UpdateInteraction(); }
    private void UpdateInteraction() => controller.Presenter.SetInteraction(this,
        !falling && !closed && (down is not null || click.IsEnabled || copying || dragging || menuOpen));
    private ContextMenu Menu()
    {
        var menu = new ContextMenu();
        Add("复制图片", async () => { if (await controller.CopyAsync(Model)) FeedbackSuccess(); });
        Add("打开", () => controller.Open(Model));
        Add("编辑（长按）", () => controller.Edit(Model));
        Add("打开所在文件夹", () => controller.Reveal(Model));
        Add("另存为…", async () => await controller.SaveAsAsync(Model));
        menu.Items.Add(new Separator());
        Add("从晾图移除（保留文件）", () => controller.Remove(Model));
        Add(Model.Item.Managed ? "将管理副本移到回收站…" : "将外部源文件移到回收站…",
            async () => await controller.RecycleAsync(Model));
        return menu;
        void Add(string title, Action action)
        { var item = new MenuItem { Header = title }; item.Click += (_, _) => action(); menu.Items.Add(item); }
    }
    internal void Place(Native.Monitor value, double x, double y)
    {
        monitor = value; screenX = value.Work.Left + (int)Math.Round(x * value.Scale);
        screenY = value.Work.Top + (int)Math.Round(y * value.Scale);
        Native.Place(this, screenX, screenY, Width, Height, value.Scale);
    }
    internal void Reveal(bool arrival = false)
    {
        if (falling) return;
        var wasHidden = !IsVisible;
        if (wasHidden) Show();
        if (!wasHidden && !arrival) return;
        Shift.BeginAnimation(TranslateTransform.YProperty, null); Shift.Y = 0;
        Card.BeginAnimation(OpacityProperty, null); Card.Opacity = 1;
        if (Theme.Motion)
        {
            Shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-40, 0,
                TimeSpan.FromMilliseconds(Theme.MotionMs)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            Nudge();
        }
    }
    internal void Retract()
    {
        click.Stop(); EndPress(); feedbackTimer.Stop(); Feedback.Visibility = Visibility.Collapsed;
        Tilt.BeginAnimation(RotateTransform.AngleProperty, null);
        Shift.BeginAnimation(TranslateTransform.YProperty, null);
        if (!IsVisible) return;
        if (!Theme.Motion) { Hide(); return; }
        var animation = new DoubleAnimation(0, -210, TimeSpan.FromMilliseconds(Theme.MotionMs));
        animation.Completed += (_, _) => { if (Shift.Y < -190) Hide(); };
        Shift.BeginAnimation(TranslateTransform.YProperty, animation);
    }
    internal void FallAndClose()
    {
        falling = true; EndPress(); hold.Stop(); click.Stop(); feedbackTimer.Stop();
        if (!IsVisible || !Theme.Motion || monitor is null) { Close(); return; }
        Height = 620; Native.Place(this, screenX, screenY, Width, Height, monitor.Scale);
        Shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, 440,
            TimeSpan.FromMilliseconds(Theme.DropMs)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(Theme.DropMs));
        fade.Completed += (_, _) => Close(); Card.BeginAnimation(OpacityProperty, fade);
    }
    internal void Nudge()
    {
        if (!IsVisible || !Theme.Motion || falling) return;
        var animation = new DoubleAnimationUsingKeyFrames();
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(Model.Tilt + 3, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(100))));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(Model.Tilt - 1.5, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(260))));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(Model.Tilt, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(500))));
        Tilt.BeginAnimation(RotateTransform.AngleProperty, animation);
    }
    private void FeedbackSuccess()
    { Feedback.Visibility = Visibility.Visible; feedbackTimer.Stop(); feedbackTimer.Start(); Nudge(); }
}
