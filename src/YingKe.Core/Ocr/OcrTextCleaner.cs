using System.Text.RegularExpressions;

namespace YingKe.Core.Ocr;

/// <summary>
/// 取字结果后处理（PRD F-14 的 M2 子集）：
/// 去首尾空白、丢空行、压缩中文字符之间被 OCR 插入的空格（保留英文/数字间的正常空格）。
/// </summary>
public static partial class OcrTextCleaner
{
    // CJK 汉字、扩展 A、CJK 标点、全角符号
    private const string CjkClass = @"\u4e00-\u9fff\u3400-\u4dbf\u3000-\u303f\uff00-\uffef";

    [GeneratedRegex($@"(?<=[{CjkClass}])[ \t]+(?=[{CjkClass}])")]
    private static partial Regex CjkInnerSpace();

    public static string Clean(IEnumerable<string> lines)
    {
        var trimmed = (lines ?? []).Select(l => l.Trim()).Where(l => l.Length > 0);
        return CjkInnerSpace().Replace(string.Join("\n", trimmed), string.Empty);
    }

    /// <summary>单行输入的便捷重载。</summary>
    public static string Clean(string text) => Clean((text ?? string.Empty).Split('\n'));
}
