using System.IO;
using System.Windows;
using System.Windows.Input;
using YingKe.App.Media;

namespace YingKe.App.UI;

/// <summary>
/// 识别结果栏（PRD F-17）：非模态浮窗，展示取字文本与引擎信息，一键复制/关闭。
/// 由 App 持有单实例；新识别或新截图时由 App 负责关闭重建。
/// </summary>
public partial class ResultBarWindow : Window
{
    public ResultBarWindow()
    {
        InitializeComponent();
        try { Icon = new System.Windows.Media.Imaging.BitmapImage(
            new Uri(Path.Combine(AppContext.BaseDirectory, "yingke.ico"))); } catch { }
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };
    }

    public void ShowLoading(string meta)
    {
        MetaText.Text = meta;
        TextBody.Visibility = Visibility.Collapsed;
        TextBody.Clear();
        CopyButton.IsEnabled = false; // 识别中结果为空，禁止复制空内容
    }

    public void ShowResult(string text, string meta)
    {
        MetaText.Text = meta;
        TextBody.Text = text;
        TextBody.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        CopyButton.IsEnabled = text.Length > 0;
    }

    private void OnHeaderDragMove(object sender, MouseButtonEventArgs e)
    {
        // 按住标题栏拖动整个结果栏；按钮点击由按钮自身处理，不会进入这里
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); }
            catch (InvalidOperationException) { }
        }
    }

    private void OnCopyClicked(object sender, RoutedEventArgs e)
    {
        if (TextBody.Text.Length == 0)
        {
            MetaText.Text = "识别尚未完成，请稍候";
            return;
        }
        try
        {
            ClipboardHelper.Gate.Wait();
            try { ClipboardHelper.SetText(TextBody.Text); }
            finally { ClipboardHelper.Gate.Release(); }
            MetaText.Text = "已复制到剪贴板";
        }
        catch (Exception ex)
        {
            MetaText.Text = $"复制失败：{ex.Message}";
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();
}
