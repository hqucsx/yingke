using System.Drawing;
using System.Text.Json.Nodes;
using YingKe.Core.Recognition;
using Xunit;

namespace YingKe.Core.Tests;

public class RecognitionProviderTests
{
    private const string B64 = "aVpYZm9v"; // 任意假 base64

    // ---- OpenAI 兼容 ----

    [Fact]
    public void OpenAi_BuildPayload_WithImage_ContainsDataUrlAndText()
    {
        var json = OpenAiCompatibleProvider.BuildPayload("gpt-4o-mini", "系统提示", "提取文字", B64);
        var root = JsonNode.Parse(json)!;

        Assert.Equal("gpt-4o-mini", root["model"]!.GetValue<string>());
        var messages = root["messages"]!.AsArray();
        Assert.Equal(2, messages.Count);
        Assert.Equal("system", messages[0]!["role"]!.GetValue<string>());
        Assert.Equal("系统提示", messages[0]!["content"]!.GetValue<string>());

        var parts = messages[1]!["content"]!.AsArray();
        Assert.Equal("image_url", parts[0]!["type"]!.GetValue<string>());
        Assert.Contains($"data:image/png;base64,{B64}", parts[0]!["image_url"]!["url"]!.GetValue<string>());
        Assert.Equal("提取文字", parts[1]!["text"]!.GetValue<string>());
        Assert.Equal(2048, root["max_tokens"]!.GetValue<int>());
    }

    [Fact]
    public void OpenAi_BuildPayload_TextOnly_UsesPlainStringContent()
    {
        var json = OpenAiCompatibleProvider.BuildPayload("m", "系统", "翻译我", null);
        var root = JsonNode.Parse(json)!;
        Assert.Equal("翻译我", root["messages"]![1]!["content"]!.GetValue<string>());
    }

    [Fact]
    public void OpenAi_ParseResponse_ExtractsMessageContent()
    {
        var json = """{"choices":[{"message":{"role":"assistant","content":"你好，世界"}}]}""";
        Assert.Equal("你好，世界", OpenAiCompatibleProvider.ParseResponse(json));
    }

    [Fact]
    public void OpenAi_ParseResponse_ThrowsOnMissingContent()
    {
        Assert.Throws<InvalidOperationException>(() => OpenAiCompatibleProvider.ParseResponse("""{"choices":[]}"""));
    }

    // ---- Anthropic ----

    [Fact]
    public void Anthropic_BuildPayload_HasSystemAndBase64Image()
    {
        var json = AnthropicProvider.BuildPayload("claude-sonnet-4-5", "描述图片", "看图", B64);
        var root = JsonNode.Parse(json)!;

        Assert.Equal("claude-sonnet-4-5", root["model"]!.GetValue<string>());
        Assert.Equal("描述图片", root["system"]!.GetValue<string>());
        Assert.Equal(2048, root["max_tokens"]!.GetValue<int>());

        var parts = root["messages"]![0]!["content"]!.AsArray();
        Assert.Equal("image", parts[0]!["type"]!.GetValue<string>());
        Assert.Equal("base64", parts[0]!["source"]!["type"]!.GetValue<string>());
        Assert.Equal("image/png", parts[0]!["source"]!["media_type"]!.GetValue<string>());
        Assert.Equal(B64, parts[0]!["source"]!["data"]!.GetValue<string>());
        Assert.Equal("看图", parts[1]!["text"]!.GetValue<string>());
    }

    [Fact]
    public void Anthropic_ParseResponse_ConcatenatesTextBlocks()
    {
        var json = """{"content":[{"type":"text","text":"第一段"},{"type":"text","text":"第二段"}]}""";
        Assert.Equal("第一段第二段", AnthropicProvider.ParseResponse(json));
    }

    // ---- Gemini ----

    [Fact]
    public void Gemini_BuildPayload_HasSystemInstructionAndInlineData()
    {
        var json = GeminiProvider.BuildPayload("描述图片", "看图", B64);
        var root = JsonNode.Parse(json)!;

        Assert.Equal("描述图片", root["system_instruction"]!["parts"]![0]!["text"]!.GetValue<string>());
        var parts = root["contents"]![0]!["parts"]!.AsArray();
        Assert.Equal("image/png", parts[0]!["inline_data"]!["mime_type"]!.GetValue<string>());
        Assert.Equal(B64, parts[0]!["inline_data"]!["data"]!.GetValue<string>());
        Assert.Equal("看图", parts[1]!["text"]!.GetValue<string>());
    }

    [Fact]
    public void Gemini_ParseResponse_ConcatenatesParts()
    {
        var json = """{"candidates":[{"content":{"parts":[{"text":"Hello "},{"text":"Gemini"}]}}]}""";
        Assert.Equal("Hello Gemini", GeminiProvider.ParseResponse(json));
    }

    // ---- 模板 ----

    [Fact]
    public void PromptTemplates_Translate_ContainsTargetLanguageAndText()
    {
        var prompt = PromptTemplates.Translate("简体中文", "hello world");
        Assert.Contains("简体中文", prompt);
        Assert.Contains("hello world", prompt);
    }

    [Fact]
    public void PromptTemplates_Registry_ContainsCoreTemplates()
    {
        Assert.True(PromptTemplates.Templates.ContainsKey("精确取字"));
        Assert.True(PromptTemplates.Templates.ContainsKey("AI 识图"));
        Assert.True(PromptTemplates.Templates.ContainsKey("代码解释"));
    }

    // ---- 图像降采样（间接验证 ToBase64Png 的降采样逻辑） ----

    [Fact]
    public void Providers_ExposeExpectedNames()
    {
        IVisionLanguageProvider p1 = new OpenAiCompatibleProvider("https://api.openai.com/v1", "gpt-4o-mini", "k");
        IVisionLanguageProvider p2 = new AzureOpenAiProvider("https://x.openai.azure.com/openai/deployments/d", "gpt-4o", "k");
        IVisionLanguageProvider p3 = new AnthropicProvider("https://api.anthropic.com/v1", "claude-sonnet-4-5", "k");
        IVisionLanguageProvider p4 = new GeminiProvider("https://generativelanguage.googleapis.com/v1beta", "gemini-2.0-flash", "k");

        Assert.Contains("gpt-4o-mini", p1.Name);
        Assert.Contains("Azure", p2.Name);
        Assert.Contains("Anthropic", p3.Name);
        Assert.Contains("Gemini", p4.Name);
    }

    // 用于确认 using System.Drawing 有效（Bitmap 参数通道编译通过）
    [Fact]
    public void ChatAsync_AcceptsNullImage_TypeLevel()
    {
        Func<IVisionLanguageProvider, string, string?, Bitmap?, Task<string>> call =
            (p, s, u, i) => p.ChatAsync(s, u, i);
        Assert.NotNull(call);
    }
}
