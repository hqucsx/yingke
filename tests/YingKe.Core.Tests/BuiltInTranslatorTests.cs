using System.Text.Json.Nodes;
using YingKe.Core.Translation;
using Xunit;

namespace YingKe.Core.Tests;

public class BuiltInTranslatorTests
{
    // ---- 语言映射 ----

    [Theory]
    [InlineData("简体中文", "zh")]
    [InlineData("English", "en")]
    [InlineData("日本語", "ja")]
    [InlineData("한국어", "ko")]
    [InlineData("Русский", "ru")]
    [InlineData("其他", "en")]
    public void ToTransmartCode_MapsSettingsLanguages(string ui, string expected)
        => Assert.Equal(expected, BuiltInTranslator.ToTransmartCode(ui));

    [Theory]
    [InlineData("简体中文", "zh-CN")]
    [InlineData("English", "en")]
    [InlineData("日本語", "ja")]
    [InlineData("其他", "en")]
    public void ToGoogleCode_MapsSettingsLanguages(string ui, string expected)
        => Assert.Equal(expected, BuiltInTranslator.ToGoogleCode(ui));

    // ---- 腾讯 Transmart ----

    [Fact]
    public void Transmart_BuildPayload_ContainsRequiredFields()
    {
        var json = BuiltInTranslator.BuildTransmartPayload("hello world", "auto", "zh");
        var root = JsonNode.Parse(json)!;

        Assert.Equal("auto_translation_block", root["header"]!["fn"]!.GetValue<string>());
        Assert.Equal("plain", root["type"]!.GetValue<string>());
        Assert.Equal("auto", root["source"]!["lang"]!.GetValue<string>());
        Assert.Equal("hello world", root["source"]!["text_block"]!.GetValue<string>());
        Assert.Equal("zh", root["target"]!["lang"]!.GetValue<string>());
    }

    [Fact]
    public void Transmart_ParseResponse_ExtractsTranslation()
    {
        var json = """{"header":{"ret_code":"succ"},"auto_translation":"那只敏捷的棕色狐狸","src_lang":"en","tgt_lang":"zh"}""";
        Assert.Equal("那只敏捷的棕色狐狸", BuiltInTranslator.ParseTransmart(json));
    }

    [Fact]
    public void Transmart_ParseResponse_ThrowsOnEmpty()
    {
        Assert.Throws<InvalidOperationException>(() => BuiltInTranslator.ParseTransmart("""{"header":{"ret_code":"succ"}}"""));
    }

    // ---- Google ----

    [Fact]
    public void Google_BuildUrl_ContainsGtxParamsAndEncodedText()
    {
        var url = BuiltInTranslator.BuildGoogleUrl("hello world", "zh-CN");
        Assert.StartsWith("https://translate.googleapis.com/translate_a/single", url);
        Assert.Contains("client=gtx", url);
        Assert.Contains("dj=1", url);
        Assert.Contains("sl=auto", url);
        Assert.Contains("tl=zh-CN", url);
        Assert.Contains("hello%20world", url);
    }

    [Fact]
    public void Google_ParseResponse_ConcatsSentences()
    {
        var json = """{"sentences":[{"trans":"敏捷的棕色狐狸","orig":"The quick brown fox"},{"trans":"跳过了懒狗","orig":"jumps over the lazy dog"}],"src":"en"}""";
        Assert.Equal("敏捷的棕色狐狸跳过了懒狗", BuiltInTranslator.ParseGoogle(json));
    }

    [Fact]
    public void Google_ParseResponse_ThrowsOnEmpty()
    {
        Assert.Throws<InvalidOperationException>(() => BuiltInTranslator.ParseGoogle("""{"sentences":[]}"""));
        Assert.Throws<InvalidOperationException>(() => BuiltInTranslator.ParseGoogle("{}"));
    }
}
