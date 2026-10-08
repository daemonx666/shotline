using System.Text.Json;

namespace Shotline;

internal sealed class Diagnostics : INotifyPropertyChanged
{
    private readonly object gate = new();
    internal string DataDirectory { get; }
    internal string LogDirectory => Path.Combine(DataDirectory, "Logs");
    internal string ImportsDirectory => Path.Combine(DataDirectory, "Imports");
    private string watch = "未启动", hotkey = "未注册", clipboard = "已关闭", persistence = "未加载", lastError = "无";
    private string logStatus = "可用（约 1 MiB × 4）";
    private bool logUnavailable;
    public string Watch { get => watch; internal set { watch = value; Changed(); } }
    public string Hotkey { get => hotkey; internal set { hotkey = value; Changed(); } }
    public string ClipboardStatus { get => clipboard; internal set { clipboard = value; Changed(); } }
    public string Persistence { get => persistence; internal set { persistence = value; Changed(); } }
    public string LastError { get => lastError; private set { lastError = value; Changed(); } }
    public string LogStatus { get => logStatus; private set { logStatus = value; Changed(); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    internal event Action<string>? ErrorRaised;

    internal Diagnostics(string? directory = null)
    {
        DataDirectory = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Shotline");
        Directory.CreateDirectory(LogDirectory); Directory.CreateDirectory(ImportsDirectory);
    }

    internal void Event(string name, Guid? id = null, string reason = "ok", int code = 0)
    {
        try
        {
            lock (gate)
            {
                var path = Path.Combine(LogDirectory, "shotline.jsonl");
                if (File.Exists(path) && new FileInfo(path).Length >= 1_048_576)
                {
                    for (var i = 3; i >= 1; i--)
                    {
                        var from = i == 1 ? path : path + "." + (i - 1);
                        if (File.Exists(from)) File.Move(from, path + "." + i, true);
                    }
                }
                File.AppendAllText(path, JsonSerializer.Serialize(new { utc = DateTimeOffset.UtcNow,
                    @event = name, itemId = id, reason, code }) + Environment.NewLine, Encoding.UTF8);
                if (logUnavailable)
                {
                    logUnavailable = false;
                    Application.Current.Dispatcher.InvokeAsync(() => LogStatus = "日志写入已恢复");
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            var first = !logUnavailable; logUnavailable = true;
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                LogStatus = $"日志不可写：{exception.GetType().Name} / 0x{exception.HResult:X8}；请检查权限和空间";
                if (first) { LastError = "日志写入失败；图片操作继续。"; ErrorRaised?.Invoke(LastError); }
            });
        }
    }
    internal void Fail(string operation, string message, Exception exception, Guid? id = null)
    {
        Event(operation, id, exception.GetType().Name, exception.HResult);
        Application.Current.Dispatcher.InvokeAsync(() => { LastError = message; ErrorRaised?.Invoke(message); });
    }
}
