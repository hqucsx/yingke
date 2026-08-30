using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace YingKe.Core.Configuration;

public enum OcrEngine
{
    /// <summary>RapidOCR（本地离线，PP-OCRv5 ONNX，首用自动下载中文模型；推荐日常使用）。</summary>
    Rapid,
    /// <summary>微信 OCR（本地，需安装微信 PC 版；首跑冷启动较慢）。</summary>
    WeChat,
    /// <summary>Windows.Media.Ocr 系统内置（离线、零依赖、兜底）。</summary>
    LocalBuiltin,
    /// <summary>云端多模态模型（走下方 AI Provider 配置）。</summary>
    CloudModel,
}

public enum AiProvider
{
    OpenAiCompatible,
    AzureOpenAi,
    Anthropic,
    Gemini,
}

public enum TranslationMode
{
    /// <summary>纯文字结果。</summary>
    TextOnly,
    /// <summary>双语对照（M3 提供）。</summary>
    Bilingual,
    /// <summary>图内文字替换（M3 提供）。</summary>
    ReplaceText,
}

public enum TranslationEngine
{
    /// <summary>AI 模型（当前配置的 Provider，质量更好，消耗额度）。</summary>
    AiModel,
    /// <summary>内置翻译（微软免费接口，直连可用、不消耗额度）。</summary>
    BuiltIn,
}

/// <summary>
/// 应用配置（PRD F-45）：JSON 落盘 %APPDATA%\YingKe\config.json；
/// API Key 不入配置文件，另存 Windows 凭据管理器（CredentialStore）。
/// </summary>
public sealed class AppConfig
{
    [JsonPropertyName("general")] public GeneralSettings General { get; set; } = new();
    [JsonPropertyName("hotkeys")] public HotkeySettings Hotkeys { get; set; } = new();
    [JsonPropertyName("ocr")] public OcrSettings Ocr { get; set; } = new();
    [JsonPropertyName("ai")] public AiSettings Ai { get; set; } = new();
    [JsonPropertyName("translation")] public TranslationSettings Translation { get; set; } = new();

    [JsonIgnore] public static string ConfigDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "YingKe");

    [JsonIgnore] public static string DefaultPath => Path.Combine(ConfigDirectory, "config.json");

    private static JsonSerializerOptions BuildOptions() => new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AppConfig Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (File.Exists(path))
            {
                var loaded = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), BuildOptions());
                if (loaded != null)
                {
                    if (MigrateSaveDirectory(loaded))
                        loaded.Save(path); // 迁移结果落盘，避免设置页与文件不一致
                    return loaded;
                }
            }
        }
        catch
        {
            // 配置损坏时回退默认值，不让设置文件挡住启动
        }
        return new AppConfig();
    }

    /// <summary>旧版默认保存目录 图片\Ta 迁移为 图片\YingKe（品牌更名）。返回是否发生了迁移。</summary>
    private static bool MigrateSaveDirectory(AppConfig config)
    {
        try
        {
            var configured = config.General.SaveDirectory;
            if (string.IsNullOrWhiteSpace(configured)) return false;
            var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (string.IsNullOrEmpty(pictures)) return false;
            var legacy = Path.GetFullPath(Path.Combine(pictures, "Ta"));
            if (string.Equals(Path.GetFullPath(configured), legacy, StringComparison.OrdinalIgnoreCase))
            {
                config.General.SaveDirectory = Path.Combine(pictures, "YingKe");
                return true;
            }
            return false;
        }
        catch
        {
            // 路径异常时不阻塞启动
            return false;
        }
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, BuildOptions()));
    }
}

public sealed class GeneralSettings
{
    /// <summary>空字符串 = 默认 图片\YingKe。</summary>
    public string SaveDirectory { get; set; } = "";
    public bool AutoStart { get; set; }
    /// <summary>取字/识图/翻译完成并复制后，结果弹窗 5 秒自动关闭（错误结果不自动关）。</summary>
    public bool AutoCloseResultBar { get; set; } = true;
    /// <summary>自动关闭的延时秒数（1–60，使用处会钳制）。</summary>
    public int AutoCloseResultSeconds { get; set; } = 5;
}

public sealed class HotkeySettings
{
    /// <summary>Win32 MOD_* 组合（Ctrl=0x2, Shift=0x4, Alt=0x1, Win=0x8）。</summary>
    public uint CaptureModifiers { get; set; } = 0x0002 | 0x0004 | 0x0001;
    /// <summary>Win32 虚拟键码，默认 '2'。</summary>
    public uint CaptureVirtualKey { get; set; } = 0x32;

    // 选区内单键快捷键（WPF Key 名称，可配置；PRD 用户反馈）
    public string OcrKey { get; set; } = "Q";        // 取字
    public string AiVisionKey { get; set; } = "I";   // AI 识图
    public string TranslateKey { get; set; } = "Y";  // 翻译
    public string PinKey { get; set; } = "P";        // 钉图
    public string SaveKey { get; set; } = "S";       // 保存
}

public sealed class OcrSettings
{
    public OcrEngine Engine { get; set; } = OcrEngine.Rapid;
}

public sealed class AiSettings
{
    public AiProvider Provider { get; set; } = AiProvider.OpenAiCompatible;
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string Model { get; set; } = "gpt-4o-mini";
    /// <summary>AI 识图当前使用的任务模板名（对应 PromptTemplates 注册表或自定义模板）。</summary>
    public string VisionTemplate { get; set; } = "AI 识图";
    /// <summary>自定义任务模板：名称 → 系统提示词。</summary>
    public Dictionary<string, string> CustomPrompts { get; set; } = new(StringComparer.Ordinal);
}

public sealed class TranslationSettings
{
    /// <summary>默认内置翻译（多服务故障转移，无需 Key）；用户配置 AI 模型后可切换。</summary>
    public TranslationEngine Engine { get; set; } = TranslationEngine.BuiltIn;
    public string TargetLanguage { get; set; } = "简体中文";
    public TranslationMode Mode { get; set; } = TranslationMode.TextOnly;
}
