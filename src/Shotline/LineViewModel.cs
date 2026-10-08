namespace Shotline;

internal sealed class CardViewModel : INotifyPropertyChanged
{
    private Screenshot item;
    private BitmapSource? thumbnail;
    internal CardViewModel(Screenshot item, BitmapSource? thumbnail) { this.item = item; this.thumbnail = thumbnail; }
    internal Screenshot Item => item;
    public Guid Id => item.Id;
    public string FileName => Path.GetFileName(item.Path);
    public string Details => $"{item.PixelWidth} × {item.PixelHeight} · {(item.Managed ? "管理副本" : "外部文件")}";
    public string Help => $"{FileName}\n{Details}\n单击复制 · 双击打开 · 长按编辑 · 拖出文件";
    public BitmapSource? Thumbnail => thumbnail;
    internal double Tilt => (item.Id.GetHashCode() % 250) / 100.0;
    internal void Update(Screenshot value, BitmapSource? image)
    { item = value; if (image is not null) thumbnail = image; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null)); }
    public event PropertyChangedEventHandler? PropertyChanged;
}

internal sealed class LineViewModel
{
    public ObservableCollection<CardViewModel> Cards { get; } = [];
    internal void Sync(IEnumerable<Screenshot> items, Guid? updated = null, BitmapSource? image = null)
    {
        var desired = items.ToList();
        foreach (var card in Cards.Where(c => desired.All(i => i.Id != c.Id)).ToArray()) Cards.Remove(card);
        for (var index = 0; index < desired.Count; index++)
        {
            var item = desired[index];
            var card = Cards.FirstOrDefault(c => c.Id == item.Id);
            if (card is null) { card = new(item, updated == item.Id ? image : null); Cards.Insert(index, card); }
            else { card.Update(item, updated == item.Id ? image : null); if (Cards.IndexOf(card) != index) Cards.Move(Cards.IndexOf(card), index); }
        }
    }
}
