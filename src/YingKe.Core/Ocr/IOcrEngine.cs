using YingKe.Core.Geometry;

namespace YingKe.Core.Ocr;

/// <summary>OCR 引擎抽象：M2 为 Windows 内置，M3 增加云端多模态与 RapidOCR 增强包。</summary>
public interface IOcrEngine
{
    string Name { get; }

    Task<OcrResult> RecognizeAsync(System.Drawing.Bitmap image, CancellationToken cancellationToken = default);
}

public sealed record OcrResult(
    string EngineName,
    string Text,
    IReadOnlyList<OcrLine> Lines,
    string LanguageTag);

public sealed record OcrLine(string Text, PixelRect Bounds);
