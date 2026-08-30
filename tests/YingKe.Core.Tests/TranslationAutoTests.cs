using YingKe.Core.Translation;
using Xunit;

namespace YingKe.Core.Tests;

public class TranslationAutoTests
{
    [Theory]
    [InlineData("简体中文")]
    [InlineData("English")]
    [InlineData("自动（中↔英互译）")]
    public void IsAuto_OnlyForAutoLabel(string target)
        => Assert.Equal(target.StartsWith("自动"), TranslationAuto.IsAuto(target));

    [Fact]
    public void Resolve_ChineseText_TranslatesToEnglish()
    {
        var target = TranslationAuto.Resolve("这是一段中文内容，包含标点。", "自动（中↔英互译）");
        Assert.Equal("English", target);
    }

    [Fact]
    public void Resolve_EnglishText_TranslatesToChinese()
    {
        var target = TranslationAuto.Resolve("The quick brown fox jumps over the lazy dog.", "自动（中↔英互译）");
        Assert.Equal("简体中文", target);
    }

    [Fact]
    public void Resolve_MixedTextPrefersDominantLanguage()
    {
        // 中文占主导（含少量英文术语）
        Assert.Equal("English", TranslationAuto.Resolve("这是一个中文句子 with some English", "自动"));
        // 英文占主导
        Assert.Equal("简体中文", TranslationAuto.Resolve("Mostly English sentence here", "自动"));
    }

    [Fact]
    public void Resolve_ExplicitTarget_PassesThrough()
    {
        Assert.Equal("English", TranslationAuto.Resolve("任何内容", "English"));
        Assert.Equal("日本語", TranslationAuto.Resolve("任何内容", "日本語"));
    }

    [Fact]
    public void Resolve_EmptyText_DefaultsToChinese()
    {
        Assert.Equal("简体中文", TranslationAuto.Resolve("", "自动"));
    }
}
