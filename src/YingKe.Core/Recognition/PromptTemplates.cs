namespace YingKe.Core.Recognition;

/// <summary>
/// 任务模板（PRD F-15）。系统提示词按任务分类；后续里程碑把模板选择暴露到 UI。
/// </summary>
public static class PromptTemplates
{
    public const string ExtractText = "精确提取图中所有文字。保持原有换行与排版顺序，输出纯文字，不要添加任何解释或前后缀。";

    public const string Describe = "描述这张图片的内容。如有文字请一并转写；如果是代码，逐段解释其功能；如果是图表，说明数据结构与结论。用简体中文回答。";

    public const string CodeExplain = "解释图中代码的功能：先一句话概括，再逐段说明关键逻辑，指出潜在问题。用简体中文回答。";

    public const string ToMarkdown = "把图中的内容转写为 Markdown。表格用 Markdown 表格，代码用代码块，保持层级结构，不要额外解释。";

    public const string ToCsv = "把图中的表格转写为 CSV。第一行为表头，使用英文逗号分隔，单元格内不得包含换行，只输出 CSV 内容。";

    public const string ToLatex = "把图中的数学公式转写为 LaTeX（行内公式用 $...$，独立公式用 $$...$$），只输出 LaTeX 代码。";

    public static string Translate(string targetLanguage, string text)
        => $"把下面的内容翻译成{targetLanguage}。只输出译文，保留原文的换行结构，不要解释、不要前后缀。\n\n{text}";

    /// <summary>模板注册表（key → 系统提示），供设置与 Agent 复用。</summary>
    public static IReadOnlyDictionary<string, string> Templates { get; } = new Dictionary<string, string>
    {
        ["精确取字"] = ExtractText,
        ["AI 识图"] = Describe,
        ["代码解释"] = CodeExplain,
        ["转 Markdown"] = ToMarkdown,
        ["转 CSV"] = ToCsv,
        ["转 LaTeX"] = ToLatex,
    };
}
