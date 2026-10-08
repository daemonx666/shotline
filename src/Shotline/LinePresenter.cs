namespace Shotline;

internal sealed class LinePresenter : IDisposable
{
    private readonly AppController controller;
    private readonly RopeWindow rope = new();
    private readonly HintWindow hint;
    private readonly Dictionary<Guid, CardWindow> cards = [];
    private readonly HashSet<CardWindow> interactions = [];
    private readonly DispatcherTimer tracking = new(), breeze = new();
    private Native.Monitor? monitor;
    private DateTimeOffset? topSince, awaySince;
    private DateTimeOffset peekUntil, checkFullscreenAt;
    private bool fullscreen, disposed, topSuppressed;
    internal bool IsRevealed { get; private set; }
    internal bool Busy { get; set; }
    private bool Interacting => Busy || interactions.Count > 0;

    internal LinePresenter(AppController controller)
    {
        this.controller = controller; hint = new(controller);
        tracking.Interval = TimeSpan.FromMilliseconds(180); tracking.Tick += (_, _) => Tick();
        breeze.Interval = TimeSpan.FromSeconds(9); breeze.Tick += (_, _) =>
        { if (IsRevealed && !Interacting && Theme.Motion) foreach (var card in cards.Values) card.Nudge(); };
    }
    internal void Initialize()
    { monitor = Native.AtPointer(); Sync(); tracking.Start(); Peek(); }
    internal void SetInteraction(CardWindow card, bool value)
    { if (value) interactions.Add(card); else interactions.Remove(card); }
    internal void Sync()
    {
        if (disposed || monitor is null) return;
        var width = monitor.Work.Width / monitor.Scale;
        controller.SetCapacity((int)((width - 160) / Theme.CardSpacing));
        foreach (var id in cards.Keys.Where(id => controller.Model.Cards.All(c => c.Id != id)).ToArray())
        { var removed = cards[id]; cards.Remove(id); removed.FallAndClose(); }
        foreach (var model in controller.Model.Cards)
        {
            if (!cards.ContainsKey(model.Id)) cards.Add(model.Id, new CardWindow(controller, model));
        }
        rope.Place(monitor);
        var count = controller.Model.Cards.Count;
        var spacing = Math.Min(Theme.CardSpacing, (width - 40) / Math.Max(1, count));
        for (var index = 0; index < count; index++)
        {
            var model = controller.Model.Cards[index];
            var x = width / 2 + (index - (count - 1) / 2.0) * spacing;
            cards[model.Id].Place(monitor, x - 95, Theme.RopeY(x, width) - 13);
        }
        hint.Place(monitor, width / 2);
        if (IsRevealed) ShowAll();
    }
    internal void Toggle()
    {
        if (disposed) return;
        if (IsRevealed) { peekUntil = DateTimeOffset.MinValue; topSuppressed = true; topSince = null; Retract(); }
        else
        { monitor = Native.AtPointer(); checkFullscreenAt = DateTimeOffset.MinValue; Sync(); peekUntil = DateTimeOffset.UtcNow.AddSeconds(4); Reveal(); }
    }
    internal void Peek()
    {
        if (disposed || Interacting) return;
        monitor = Native.AtPointer(); checkFullscreenAt = DateTimeOffset.MinValue;
        Sync(); peekUntil = DateTimeOffset.UtcNow.AddMilliseconds(Theme.PeekMs); Reveal();
    }
    internal void Notify(string text)
    { if (disposed) return; hint.SetMessage(text); peekUntil = DateTimeOffset.UtcNow.AddSeconds(5); Reveal(); }
    internal void RefreshEnvironment()
    {
        if (disposed) return;
        checkFullscreenAt = DateTimeOffset.MinValue;
        if (monitor is not null)
        {
            try { monitor = Native.ReadMonitor(monitor.Handle); }
            catch (Win32Exception) { monitor = Native.AtPointer(); }
        }
        Sync(); Tick();
    }
    private void Tick()
    {
        if (disposed || monitor is null) return;
        var now = DateTimeOffset.UtcNow;
        Native.GetCursorPos(out var pointer);
        if (now >= checkFullscreenAt)
        {
            var previous = fullscreen; fullscreen = Native.IsFullscreen(monitor);
            checkFullscreenAt = now.AddMilliseconds(700);
            if (previous != fullscreen) controller.Diagnostics.Event("fullscreen.state", reason: fullscreen ? "hidden" : "available");
            if (fullscreen && IsRevealed && !Interacting) Retract();
        }
        if (Interacting) { awaySince = null; return; }
        var pointerMonitor = Native.AtPointer();
        var atTop = pointer.X >= pointerMonitor.Work.Left && pointer.X < pointerMonitor.Work.Right &&
            pointer.Y >= pointerMonitor.Work.Top && pointer.Y <= pointerMonitor.Work.Top + (int)(5 * pointerMonitor.Scale);
        if (atTop && !topSuppressed)
        {
            topSince ??= now;
            if ((now - topSince.Value).TotalMilliseconds >= Theme.RevealDelayMs)
            {
                if (monitor.Handle != pointerMonitor.Handle)
                { monitor = pointerMonitor; Sync(); fullscreen = Native.IsFullscreen(monitor); }
                if (!fullscreen) Reveal();
            }
        }
        else { topSince = null; if (!atTop) topSuppressed = false; }
        if (!IsRevealed) return;
        var inZone = pointer.X >= monitor.Work.Left && pointer.X < monitor.Work.Right &&
            pointer.Y >= monitor.Bounds.Top && pointer.Y <= monitor.Work.Top + Theme.LineHeight * monitor.Scale;
        if (inZone || now < peekUntil) awaySince = null;
        else
        {
            awaySince ??= now;
            if ((now - awaySince.Value).TotalMilliseconds >= Theme.RetractDelayMs) Retract();
        }
    }
    private void Reveal()
    {
        if (monitor is null) return;
        if (IsRevealed) { awaySince = null; return; }
        if (Native.IsFullscreen(monitor)) return;
        IsRevealed = true; awaySince = null;
        tracking.Interval = TimeSpan.FromMilliseconds(80); ShowAll();
        if (Theme.Motion) breeze.Start(); else breeze.Stop();
    }
    private void ShowAll()
    {
        rope.Reveal(true);
        foreach (var card in cards.Values) card.Reveal();
        if (cards.Count == 0 || hint.HasMessage) hint.ShowHint(); else hint.Hide();
    }
    private void Retract()
    {
        IsRevealed = false; awaySince = null; breeze.Stop(); hint.ClearMessage(); hint.Hide(); rope.Reveal(false);
        foreach (var card in cards.Values) card.Retract();
        tracking.Interval = TimeSpan.FromMilliseconds(180);
    }
    public void Dispose()
    {
        disposed = true; tracking.Stop(); breeze.Stop(); rope.Close(); hint.Close();
        foreach (var card in cards.Values) card.Close(); cards.Clear(); interactions.Clear();
    }
}
