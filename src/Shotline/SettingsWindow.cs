using Microsoft.Win32;
using System.Windows.Data;

namespace Shotline;

internal sealed class SettingsWindow : Window
{
    private readonly AppController controller;
    private readonly TextBox folder = new(), editor = new(), shortcut = new();
    private readonly CheckBox watch = new() { Content = "监听截图文件夹" },
        clipboard = new() { Content = "导入剪贴板图片（也会接收普通复制图片）" },
        sound = new() { Content = "操作声音" }, startup = new() { Content = "登录 Windows 时启动（默认关闭）" },
        motion = new() { Content = "减少动画（系统关闭动画时自动遵循）" };
    private readonly ComboBox theme = new() { ItemsSource = new[] { "跟随系统", "浅色 · 暖纸", "深色 · 松墨" }, MinHeight = 32 };
    private readonly TextBlock result = new() { Margin = new Thickness(0, 8, 0, 8) };
    private readonly Button save = new() { Content = "保存设置", IsDefault = true };
    internal SettingsWindow(AppController controller)
    {
        this.controller = controller; Title = "Shotline · 晾图设置"; Width = 560; Height = 740;
        MinWidth = 470; MinHeight = 460; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "Background");
        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/Shotline.ico"));
        var root = new DockPanel { Margin = new Thickness(24, 20, 24, 18) };
        var bottom = new StackPanel(); DockPanel.SetDock(bottom, Dock.Bottom);
        bottom.Children.Add(result);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(save); var close = new Button { Content = "关闭", IsCancel = true };
        close.Click += (_, _) => Close(); buttons.Children.Add(close); bottom.Children.Add(buttons); root.Children.Add(bottom);
        var content = new StackPanel();
        var scroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; root.Children.Add(scroll); Content = root;
        content.Children.Add(new TextBlock { Text = "晾图", FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 27, FontWeight = FontWeights.SemiBold });
        var subtitle = new TextBlock { Text = "让截图暂存片刻，用完就取下。", Margin = new Thickness(0, 4, 0, 16) };
        subtitle.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); content.Children.Add(subtitle);
        Label("截图来源"); content.Children.Add(watch);
        InputRow(folder, "选择目录…", () =>
        {
            var dialog = new OpenFolderDialog { Title = "选择截图监听目录" };
            if (Directory.Exists(folder.Text)) dialog.InitialDirectory = folder.Text;
            if (dialog.ShowDialog(this) == true) folder.Text = dialog.FolderName;
        });
        content.Children.Add(clipboard);
        var import = new Button { Content = "手动导入图片…", HorizontalAlignment = HorizontalAlignment.Left };
        import.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog { Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp", Multiselect = true };
            if (dialog.ShowDialog(this) == true) controller.AddFiles(dialog.FileNames);
        }; content.Children.Add(import);
        Label("全局快捷键"); content.Children.Add(shortcut);
        content.Children.Add(new TextBlock { Text = "示例：Ctrl+Alt+T / Ctrl+Shift+F10；保存后即时注册。", FontSize = 11,
            Margin = new Thickness(0, 4, 0, 4) });
        Label("外部编辑器"); InputRow(editor, "选择程序…", () =>
        {
            var dialog = new OpenFileDialog { Filter = "程序|*.exe", Title = "选择外部图片编辑器" };
            if (dialog.ShowDialog(this) == true) editor.Text = dialog.FileName;
        });
        content.Children.Add(new TextBlock { Text = "长按会将文件路径作为一个参数传给编辑器。请保存原文件，缩略图会自动刷新。", FontSize = 11 });
        Label("外观与桌面"); content.Children.Add(theme); content.Children.Add(sound); content.Children.Add(motion); content.Children.Add(startup);
        Label("运行状态"); Status("文件监听", nameof(Diagnostics.Watch)); Status("剪贴板", nameof(Diagnostics.ClipboardStatus));
        Status("快捷键", nameof(Diagnostics.Hotkey)); Status("配置与列表", nameof(Diagnostics.Persistence)); Status("最近错误", nameof(Diagnostics.LastError));
        Status("日志", nameof(Diagnostics.LogStatus));
        var directories = new WrapPanel();
        DirectoryButton("数据目录", controller.Diagnostics.DataDirectory);
        DirectoryButton("导入副本", controller.Diagnostics.ImportsDirectory);
        DirectoryButton("日志目录", controller.Diagnostics.LogDirectory); content.Children.Add(directories);
        Label("文件边界"); content.Children.Add(new TextBlock { FontSize = 11,
            Text = "关闭卡片、清空晾图与容量淘汰只移除展示记录，保留文件。回收站是独立操作；管理副本也不会自动清理。" });
        var clear = new Button { Content = "清空晾图（保留全部文件）", HorizontalAlignment = HorizontalAlignment.Left };
        clear.Click += (_, _) => controller.Clear(); content.Children.Add(clear);
        var config = controller.Config;
        folder.Text = config.WatchFolder; editor.Text = config.EditorPath; shortcut.Text = config.HotKey;
        watch.IsChecked = config.WatchEnabled; clipboard.IsChecked = config.ClipboardEnabled;
        sound.IsChecked = config.SoundEnabled; startup.IsChecked = config.LaunchAtLogin; motion.IsChecked = config.ReduceMotion;
        theme.SelectedIndex = (int)config.Theme;
        save.Click += Save;

        void Label(string text) => content.Children.Add(new TextBlock { Text = text, FontWeight = FontWeights.SemiBold,
            FontSize = 13, Margin = new Thickness(0, 15, 0, 6) });
        void InputRow(TextBox input, string text, Action action)
        {
            var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            input.Margin = new Thickness(0, 3, 8, 3); grid.Children.Add(input);
            var button = new Button { Content = text, Margin = new Thickness(0, 3, 0, 3) };
            Grid.SetColumn(button, 1); button.Click += (_, _) => action(); grid.Children.Add(button); content.Children.Add(grid);
        }
        void Status(string title, string property)
        {
            content.Children.Add(new TextBlock { Text = title, FontSize = 11, Margin = new Thickness(0, 8, 0, 2) });
            var value = new TextBlock { FontSize = 12 }; value.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            value.SetBinding(TextBlock.TextProperty, new Binding(property) { Source = controller.Diagnostics }); content.Children.Add(value);
        }
        void DirectoryButton(string text, string path)
        { var button = new Button { Content = text, FontSize = 11 }; button.Click += (_, _) => controller.OpenDirectory(path); directories.Children.Add(button); }
    }
    private async void Save(object sender, RoutedEventArgs e)
    {
        save.IsEnabled = false; result.Text = "正在保存…";
        try
        {
            await controller.ApplyConfigAsync(controller.Config with { WatchFolder = folder.Text.Trim(),
                EditorPath = editor.Text.Trim(), HotKey = shortcut.Text.Trim(), WatchEnabled = watch.IsChecked == true,
                ClipboardEnabled = clipboard.IsChecked == true, SoundEnabled = sound.IsChecked == true,
                LaunchAtLogin = startup.IsChecked == true, ReduceMotion = motion.IsChecked == true,
                Theme = (ColorTheme)theme.SelectedIndex });
            result.Text = controller.Diagnostics.Hotkey.StartsWith("注册失败", StringComparison.Ordinal)
                ? "配置已应用；快捷键注册失败，请修改后再保存。" : "配置已应用。";
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException)
        { result.Text = "未完成：" + ex.Message; controller.Diagnostics.Fail("config.save", "设置无法保存，请检查输入及权限。", ex); }
        finally { save.IsEnabled = true; }
    }
}
