using System.Text.RegularExpressions;

namespace YingKe.Core.Translation;

/// <summary>
/// 翻译源语言检测（中英互译模式）：
/// 目标语言配置为“自动”时，按 OCR 原文的语言自动决定翻译方向——
/// 中文为主 → 翻成英文；否则 → 翻成简体中文。
/// </summary>
public static partial class TranslationAuto
{
    public const string AutoLabel = "自动（中↔英互译）";

    [GeneratedRegex(@"[\u4e00-\u9fff\u3400-\u4dbf]")]
    private static partial Regex CjkChar();

    [GeneratedRegex(@"[A-Za-z]")]
    private static partial Regex LatinChar();

    public static bool IsAuto(string targetLanguage)
        => !string.IsNullOrEmpty(targetLanguage) && targetLanguage.StartsWith("自动");

    /// <summary>解析实际目标语言：显式配置原样返回；auto 按原文语言判定互译方向。</summary>
    public static string Resolve(string ocrText, string configuredTarget)
    {
        if (!IsAuto(configuredTarget))
            return configuredTarget;

        return IsMostlyChinese(ocrText) ? "English" : "简体中文";
    }

    /// <summary>
    /// 混合文本的语言判定：CJK 字符达到拉丁字母的 30% 即视为中文原文
    /// （中文技术文档常夹英文术语，按字符数简单多数会误判）。
    /// </summary>
    public static bool IsMostlyChinese(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        int cjk = CjkChar().Matches(text).Count;
        int latin = LatinChar().Matches(text).Count;
        return cjk > 0 && cjk * 10 >= latin * 3;
    }
}
