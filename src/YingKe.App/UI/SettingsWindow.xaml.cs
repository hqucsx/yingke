using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using System.Windows.Input;
using YingKe.Core.Configuration;
using YingKe.Core.Files;
using YingKe.Core.Platform;
using YingKe.Core.Security;

namespace YingKe.App.UI;

/// <summary>
/// 设置中心 v1（PRD F-40~F45 的 M2 子集提前落地）：
/// 通用 / 截图快捷键 / OCR 与 AI / 翻译 / Agent。
/// 保存时统一写配置并即时生效（快捷键换绑冲突则保留原键并提示）。
/// </summary>
public partial class SettingsWindow : Window
{
    private const string ApiKeyCredentialName = "ai.apikey";

    private readonly AppConfig _config;
    private readonly Func<uint, uint, bool> _applyCaptureHotkey;

    private uint _pendingModifiers;
    private uint _pendingVirtualKey;
    private bool _pendingHotkeyValid;

    public SettingsWindow(AppConfig config, Func<uint, uint, bool> applyCaptureHotkey)
    {
        InitializeComponent();
        try { Icon = new System.Windows.Media.Imaging.BitmapImage(
            new Uri(Path.Combine(AppContext.BaseDirectory, "yingke.ico"))); } catch { }

        // 录制框禁用输入法：中文 IME 会把按键吞成 ImeProcessed，导致录出 "ImePrc" 这类无效键名
        foreach (var box in new System.Windows.Controls.TextBox[]
                 { HotkeyBox, OcrKeyBox, AiVisionKeyBox, TranslateKeyBox, PinKeyBox, SaveKeyBox })
            System.Windows.Input.InputMethod.SetIsInputMethodEnabled(box, false);
        _config = config;
        _applyCaptureHotkey = applyCaptureHotkey;
        LoadValues();
    }

    // ---- 载入 ----

    private void LoadValues()
    {
        AutoStartCheck.IsChecked = AutoStart.IsEnabled();
        AutoCloseResultCheck.IsChecked = _config.General.AutoCloseResultBar;
        AutoCloseSecondsBox.Text = Math.Clamp(_config.General.AutoCloseResultSeconds, 1, 60).ToString();
        SaveDirBox.Text = ResolveSaveDirectory();

        LoadHotkeyFromConfig();
        LoadOverlayKeys();

        // UI 顺序（Rapid/微信/本地/云端）与枚举顺序不同，显式映射
        OcrEngineCombo.SelectedIndex = _config.Ocr.Engine switch
        {
            OcrEngine.Rapid => 0,
            OcrEngine.WeChat => 1,
            OcrEngine.CloudModel => 3,
            _ => 2,
        };
        AiProviderCombo.SelectedIndex = (int)_config.Ai.Provider;
        BaseUrlBox.Text = _config.Ai.BaseUrl;
        ModelBox.Text = _config.Ai.Model;
        UpdateKeyStatus();

        var languages = TargetLanguageCombo.Items.Cast<ComboBoxItem>().ToList();
        var match = languages.FirstOrDefault(i => i.Content as string == _config.Translation.TargetLanguage);
        TargetLanguageCombo.SelectedItem = match ?? languages[0];
        TranslationEngineCombo.SelectedIndex = (int)_config.Translation.Engine;
        TranslationModeCombo.SelectedIndex = (int)_config.Translation.Mode;
        LoadCustomTemplates();
    }

    private void LoadCustomTemplates()
    {
        var selected = CustomTplCombo.Text;
        CustomTplCombo.Items.Clear();
        foreach (var kv in _config.Ai.CustomPrompts)
            CustomTplCombo.Items.Add(kv.Key);
        CustomTplCombo.SelectedIndex = CustomTplCombo.Items.Count > 0 ? 0 : -1;
    }

    private void LoadOverlayKeys()
    {
        OcrKeyBox.Text = _config.Hotkeys.OcrKey;
        AiVisionKeyBox.Text = _config.Hotkeys.AiVisionKey;
        TranslateKeyBox.Text = _config.Hotkeys.TranslateKey;
        PinKeyBox.Text = _config.Hotkeys.PinKey;
        SaveKeyBox.Text = _config.Hotkeys.SaveKey;
    }

    private void OnOverlayKeyPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            LoadOverlayKeys();
            Keyboard.ClearFocus();
            return;
        }
        if (key is Key.Tab or Key.Enter or Key.Return) return;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
        {
            box.Text = "修饰键…";
            return;
        }

        box.Text = KeyToConfigName(key);
    }

    private static string KeyToConfigName(Key key)
    {
        var name = key.ToString();
        return name.Length == 2 && name[0] == 'D' && char.IsAsciiDigit(name[1]) ? name[1..] : name;
    }

    private bool SaveOverlayKeys()
    {
        var boxes = new (TextBox Box, string Label, string? Value)[]
        {
            (OcrKeyBox, "取字", _config.Hotkeys.OcrKey),
            (AiVisionKeyBox, "识图", _config.Hotkeys.AiVisionKey),
            (TranslateKeyBox, "翻译", _config.Hotkeys.TranslateKey),
            (PinKeyBox, "钉图", _config.Hotkeys.PinKey),
            (SaveKeyBox, "保存", _config.Hotkeys.SaveKey),
        };

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (box, label, _) in boxes)
        {
            if (string.IsNullOrWhiteSpace(box.Text)) continue;
            if (values.TryGetValue(box.Text, out var first))
            {
                MessageBox.Show(this, $"“{first}”和“{label}”都设成了 {box.Text}，请换一个键。", "映刻 设置",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            values[box.Text] = label;
        }
        foreach (var (box, label, _) in boxes)
            if (!string.IsNullOrWhiteSpace(box.Text))
                AssignOverlayKey(label, box.Text);
        return true;
    }

    private void AssignOverlayKey(string label, string value)
    {
        switch (label)
        {
            case "取字": _config.Hotkeys.OcrKey = value; break;
            case "识图": _config.Hotkeys.AiVisionKey = value; break;
            case "翻译": _config.Hotkeys.TranslateKey = value; break;
            case "钉图": _config.Hotkeys.PinKey = value; break;
            case "保存": _config.Hotkeys.SaveKey = value; break;
        }
    }

    private void LoadHotkeyFromConfig()
    {
        _pendingModifiers = _config.Hotkeys.CaptureModifiers;
        _pendingVirtualKey = _config.Hotkeys.CaptureVirtualKey;
        _pendingHotkeyValid = true;
        HotkeyBox.Text = HotkeyGesture.Describe(_pendingModifiers, _pendingVirtualKey);
    }

    private string ResolveSaveDirectory()
    {
        var configured = _config.General.SaveDirectory;
        return string.IsNullOrWhiteSpace(configured) ? SavePathGenerator.DefaultDirectory() : configured;
    }

    // ---- 快捷键录制 ----

    private void OnHotkeyGotFocus(object sender, RoutedEventArgs e)
        => HotkeyBox.Text = "按下新组合键…（Esc 取消）";

    private void OnHotkeyLostFocus(object sender, RoutedEventArgs e)
    {
        // 只还原显示、不丢弃待保存的组合：
        // 点击“保存修改”会先触发失焦，若在此重置待保存值，编辑永远无法落盘。
        HotkeyBox.Text = _pendingHotkeyValid
            ? HotkeyGesture.Describe(_pendingModifiers, _pendingVirtualKey)
            : HotkeyGesture.Describe(_config.Hotkeys.CaptureModifiers, _config.Hotkeys.CaptureVirtualKey);
    }

    private void OnHotkeyPreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            LoadHotkeyFromConfig();
            Keyboard.ClearFocus();
            return;
        }

        var modifiers = HotkeyGesture.ToWin32Modifiers(Keyboard.Modifiers);
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
        {
            // 只按下了修饰键：显示当前组合但尚未生效
            _pendingHotkeyValid = false;
            HotkeyBox.Text = HotkeyGesture.Describe(modifiers, 0) + "…";
            return;
        }

        if (key is Key.Tab or Key.Enter or Key.Return)
            return;

        _pendingModifiers = modifiers;
        _pendingVirtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        _pendingHotkeyValid = true;
        HotkeyBox.Text = HotkeyGesture.Describe(_pendingModifiers, _pendingVirtualKey);
    }

    // ---- 保存 ----

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        if (!SaveAll())
        {
            MessageBox.Show(this, "设置未保存，请先解决上述问题。", "映刻 设置",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        SaveStatus.Text = $"已保存 {DateTime.Now:HH:mm:ss}";
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();

    private void OnCustomTplSelected(object sender, SelectionChangedEventArgs e)
    {
        if (CustomTplCombo.SelectedItem is string name && _config.Ai.CustomPrompts.TryGetValue(name, out var prompt))
        {
            TplNameBox.Text = name;
            TplPromptBox.Text = prompt;
        }
    }

    private void OnSaveTemplateClicked(object sender, RoutedEventArgs e)
    {
        var name = TplNameBox.Text.Trim();
        var prompt = TplPromptBox.Text.Trim();
        if (name.Length == 0 || prompt.Length == 0)
        {
            MessageBox.Show(this, "请填写模板名称和提示词。", "映刻 设置",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _config.Ai.CustomPrompts[name] = prompt;
        _config.Save();
        TplNameBox.Clear();
        TplPromptBox.Clear();
        LoadCustomTemplates();
    }

    private void OnDeleteTemplateClicked(object sender, RoutedEventArgs e)
    {
        var name = TplNameBox.Text.Trim();
        if (name.Length == 0) return;
        if (_config.Ai.CustomPrompts.Remove(name))
        {
            _config.Save();
            LoadCustomTemplates();
        }
    }

    private bool SaveAll()
    {
        // 0) 选区内快捷键（缺失调用曾导致修改不生效）
        if (!SaveOverlayKeys())
            return false;

        // 1) 快捷键：先试注册，失败保留原键
        if (!_pendingHotkeyValid)
        {
            MessageBox.Show(this, "快捷键不完整：请包含至少一个非修饰键。", "映刻 设置",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        if (_pendingModifiers != _config.Hotkeys.CaptureModifiers ||
            _pendingVirtualKey != _config.Hotkeys.CaptureVirtualKey)
        {
            if (!_applyCaptureHotkey(_pendingModifiers, _pendingVirtualKey))
            {
                MessageBox.Show(this,
                    $"新快捷键 {HotkeyGesture.Describe(_pendingModifiers, _pendingVirtualKey)} 被其他程序占用，已保留原快捷键。",
                    "快捷键冲突", MessageBoxButton.OK, MessageBoxImage.Warning);
                LoadHotkeyFromConfig();
                return false;
            }
        }

        // 2) 通用
        var autoStart = AutoStartCheck.IsChecked == true;
        if (autoStart != AutoStart.IsEnabled())
            AutoStart.SetEnabled(autoStart);
        _config.General.AutoStart = autoStart;
        _config.General.AutoCloseResultBar = AutoCloseResultCheck.IsChecked == true;

        if (!int.TryParse(AutoCloseSecondsBox.Text.Trim(), out var autoCloseSeconds))
        {
            MessageBox.Show(this, "自动关闭延时须为数字（1–60 秒）。", "映刻 设置",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        _config.General.AutoCloseResultSeconds = Math.Clamp(autoCloseSeconds, 1, 60);

        var saveDir = SaveDirBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(saveDir))
        {
            try { Directory.CreateDirectory(saveDir); }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"保存目录无效：{ex.Message}", "映刻 设置",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }
        _config.General.SaveDirectory = saveDir;

        // 3) OCR 与 AI
        _config.Ocr.Engine = OcrEngineCombo.SelectedIndex switch
        {
            0 => OcrEngine.Rapid,
            1 => OcrEngine.WeChat,
            3 => OcrEngine.CloudModel,
            _ => OcrEngine.LocalBuiltin,
        };
        _config.Ai.Provider = (AiProvider)Math.Max(0, AiProviderCombo.SelectedIndex);
        _config.Ai.BaseUrl = BaseUrlBox.Text.Trim();
        _config.Ai.Model = ModelBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(KeyBox.Password))
        {
            CredentialStore.Save(ApiKeyCredentialName, KeyBox.Password.Trim());
            KeyBox.Clear();
        }

        // 4) 翻译（双语/替换两项 M3 前被禁用，索引不会越界）
        _config.Translation.Engine = (TranslationEngine)Math.Max(0, TranslationEngineCombo.SelectedIndex);
        var lang = (TargetLanguageCombo.SelectedItem as ComboBoxItem)?.Content as string;
        _config.Translation.TargetLanguage = lang ?? "简体中文";
        _config.Translation.Mode = (TranslationMode)Math.Max(0, TranslationModeCombo.SelectedIndex);

        _config.Save();
        UpdateKeyStatus();
        return true;
    }

    private void UpdateKeyStatus()
        => KeyStatus.Text = CredentialStore.Exists(ApiKeyCredentialName)
            ? "已保存到 Windows 凭据管理器（不回显明文）"
            : "未设置";

    // ---- 单项动作 ----

    private void OnOpenConfigFolderClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppConfig.ConfigDirectory);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{AppConfig.ConfigDirectory}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"打开文件夹失败：{ex.Message}", "映刻 设置",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnBrowseClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择默认保存目录",
        };
        if (Directory.Exists(SaveDirBox.Text))
            dialog.InitialDirectory = SaveDirBox.Text;
        if (dialog.ShowDialog(this) == true)
            SaveDirBox.Text = dialog.FolderName;
    }

    private void OnClearKeyClicked(object sender, RoutedEventArgs e)
    {
        CredentialStore.Delete(ApiKeyCredentialName);
        KeyBox.Clear();
        UpdateKeyStatus();
    }

    private void OnProviderChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BaseUrlBox == null) return;
        BaseUrlBox.Text = (AiProviderCombo.SelectedIndex) switch
        {
            (int)AiProvider.OpenAiCompatible => "https://api.openai.com/v1",
            (int)AiProvider.AzureOpenAi => "https://<你的资源名>.openai.azure.com/openai/deployments/<部署名>",
            (int)AiProvider.Anthropic => "https://api.anthropic.com/v1",
            (int)AiProvider.Gemini => "https://generativelanguage.googleapis.com/v1beta",
            _ => BaseUrlBox.Text,
        };
    }
}
