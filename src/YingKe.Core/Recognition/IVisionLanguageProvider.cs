using System.Drawing;

namespace YingKe.Core.Recognition;

/// <summary>
/// 视觉-语言模型客户端抽象：一张图 + 可选提示词 → 文本。
/// 覆盖取字（云端）、AI 识图、翻译三类调用。
/// </summary>
public interface IVisionLanguageProvider
{
    string Name { get; }

    /// <param name="systemPrompt">系统提示（任务模板）</param>
    /// <param name="userText">用户文本（翻译原文等；可为空）</param>
    /// <param name="image">随附图像（可为空 = 纯文本对话）</param>
    Task<string> ChatAsync(string systemPrompt, string? userText, Bitmap? image, CancellationToken cancellationToken = default);
}
