using YingKe.Core.Configuration;
using Xunit;

namespace YingKe.Core.Tests;

public class AppConfigTests : IDisposable
{
    private readonly string _path =
        Path.Combine(Path.GetTempPath(), $"ta-config-test-{Guid.NewGuid():N}.json");

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var config = AppConfig.Load(_path);

        Assert.Equal(OcrEngine.Rapid, config.Ocr.Engine);
        Assert.Equal(AiProvider.OpenAiCompatible, config.Ai.Provider);
        Assert.Equal(TranslationEngine.BuiltIn, config.Translation.Engine);
        Assert.Equal(TranslationMode.TextOnly, config.Translation.Mode);
        Assert.Equal("简体中文", config.Translation.TargetLanguage);
        Assert.True(string.IsNullOrEmpty(config.General.SaveDirectory));
        // 默认快捷键 Ctrl+Shift+Alt+2
        Assert.Equal(0x0002u | 0x0004u | 0x0001u, config.Hotkeys.CaptureModifiers);
        Assert.Equal(0x32u, config.Hotkeys.CaptureVirtualKey);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAllSettings()
    {
        var original = new AppConfig();
        original.General.SaveDirectory = @"D:\shots";
        original.General.AutoStart = true;
        original.Hotkeys.CaptureModifiers = 0x0008; // Win
        original.Hotkeys.CaptureVirtualKey = 0x41;  // A
        original.Ocr.Engine = OcrEngine.CloudModel;
        original.Ai.Provider = AiProvider.Anthropic;
        original.Ai.BaseUrl = "https://api.anthropic.com/v1";
        original.Ai.Model = "claude-sonnet-4-5";
        original.Translation.TargetLanguage = "English";
        original.Translation.Mode = TranslationMode.Bilingual;

        original.Save(_path);
        var loaded = AppConfig.Load(_path);

        Assert.Equal(original.General.SaveDirectory, loaded.General.SaveDirectory);
        Assert.Equal(original.General.AutoStart, loaded.General.AutoStart);
        Assert.Equal(original.Hotkeys.CaptureModifiers, loaded.Hotkeys.CaptureModifiers);
        Assert.Equal(original.Hotkeys.CaptureVirtualKey, loaded.Hotkeys.CaptureVirtualKey);
        Assert.Equal(original.Ocr.Engine, loaded.Ocr.Engine);
        Assert.Equal(original.Ai.Provider, loaded.Ai.Provider);
        Assert.Equal(original.Ai.BaseUrl, loaded.Ai.BaseUrl);
        Assert.Equal(original.Ai.Model, loaded.Ai.Model);
        Assert.Equal(original.Translation.TargetLanguage, loaded.Translation.TargetLanguage);
        Assert.Equal(original.Translation.Mode, loaded.Translation.Mode);
    }

    [Fact]
    public void SaveThenLoad_WeChatEngine_RoundTrips()
    {
        var original = new AppConfig();
        original.Ocr.Engine = OcrEngine.WeChat;
        original.Save(_path);
        var loaded = AppConfig.Load(_path);
        Assert.Equal(OcrEngine.WeChat, loaded.Ocr.Engine);
    }

    [Fact]
    public void Load_CorruptedFile_FallsBackToDefaults()
    {
        File.WriteAllText(_path, "{ not valid json !!!");
        var config = AppConfig.Load(_path);
        Assert.Equal(OcrEngine.Rapid, config.Ocr.Engine);
    }

    public void Dispose() => File.Delete(_path);
}
