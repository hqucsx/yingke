using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using YingKe.Core.Geometry;
using WinOcr = Windows.Media.Ocr.OcrEngine;

namespace YingKe.Core.Ocr;

/// <summary>
/// Windows 内置 OCR（PRD F-10：零依赖、离线）。
/// GDI Bitmap → PNG 内存流 → SoftwareBitmap → RecognizeAsync；
/// 超过引擎 MaxImageDimension 的图像先等比缩小。
/// </summary>
public sealed class WindowsOcrEngine : IOcrEngine
{
    public string Name => "Windows 内置 OCR";

    /// <summary>系统是否装了任何 OCR 语言包。</summary>
    public static bool IsAvailable() => WinOcr.AvailableRecognizerLanguages.Any();

    public async Task<OcrResult> RecognizeAsync(Bitmap image, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);

        // 小图 2 倍放大：Windows OCR 对 100% DPI 下的小字（<500px 高）识别很差，
        // 高质量双三次放大后显著提升（标准做法）
        Bitmap ocrSource = image;
        bool upsampled = false;
        if (image.Height < 500 && image.Width < 1600)
        {
            ocrSource = new Bitmap(image.Width * 2, image.Height * 2);
            using var g = System.Drawing.Graphics.FromImage(ocrSource);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(image, 0, 0, ocrSource.Width, ocrSource.Height);
            upsampled = true;
        }

        try
        {
            var language = ResolveLanguage()
                ?? throw new InvalidOperationException(
                    "系统未安装 OCR 语言包：设置 → 时间和语言 → 语言和区域 → 添加语言（如中文），并勾选“文本提取（OCR）”可选功能。");
            var engine = WinOcr.TryCreateFromLanguage(language)
                ?? throw new InvalidOperationException($"无法创建 OCR 引擎（语言 {language.LanguageTag}）。");

            using var softwareBitmap = await ToSoftwareBitmapAsync(ocrSource, WinOcr.MaxImageDimension);
            var winResult = await engine.RecognizeAsync(softwareBitmap);

            var lines = winResult.Lines.Select(line =>
            {
                var words = line.Words.ToList();
                if (!words.Any())
                    return new OcrLine(line.Text, default(PixelRect));
                double x1 = words.Min(w => w.BoundingRect.X);
                double y1 = words.Min(w => w.BoundingRect.Y);
                double x2 = words.Max(w => w.BoundingRect.Right);
                double y2 = words.Max(w => w.BoundingRect.Bottom);
                // 坐标换算回原图（放大过则除以 2）
                int div = upsampled ? 2 : 1;
                return new OcrLine(line.Text, new PixelRect((int)x1 / div, (int)y1 / div,
                    (int)(x2 - x1) / div, (int)(y2 - y1) / div));
            }).ToList();

            return new OcrResult(Name, OcrTextCleaner.Clean(lines.Select(l => l.Text)), lines, language.LanguageTag);
        }
        finally
        {
            if (upsampled) ocrSource.Dispose();
        }
    }

    /// <summary>优先简体中文，其次系统首选语言，最后任意可用语言。</summary>
    private static Language? ResolveLanguage()
    {
        foreach (var tag in new[] { "zh-Hans", "zh-CN" })
        {
            var candidate = new Language(tag);
            if (WinOcr.IsLanguageSupported(candidate))
                return candidate;
        }

        var preferred = Windows.System.UserProfile.GlobalizationPreferences.Languages.FirstOrDefault();
        if (preferred != null)
        {
            var candidate = new Language(preferred);
            if (WinOcr.IsLanguageSupported(candidate))
                return candidate;
        }

        return WinOcr.AvailableRecognizerLanguages.FirstOrDefault();
    }

    private static async Task<SoftwareBitmap> ToSoftwareBitmapAsync(Bitmap image, uint maxDimension)
    {
        double scale = Math.Min(
            1.0,
            Math.Min((double)maxDimension / Math.Max(1, image.Width), (double)maxDimension / Math.Max(1, image.Height)));
        int targetWidth = Math.Max(1, (int)Math.Round(image.Width * scale));
        int targetHeight = Math.Max(1, (int)Math.Round(image.Height * scale));

        using var scaled = scale < 1.0 ? Resize(image, targetWidth, targetHeight) : null;
        var source = scaled ?? image;
        var bgra = ExtractBgra(source);

        var stream = new InMemoryRandomAccessStream();
        try
        {
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                (uint)source.Width, (uint)source.Height, 96, 96, bgra);
            await encoder.FlushAsync();

            stream.Seek(0);
            var decoder = await BitmapDecoder.CreateAsync(stream);
            return await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        }
        finally
        {
            stream.Dispose();
        }
    }

    private static Bitmap Resize(Bitmap source, int width, int height)
    {
        var destination = new Bitmap(width, height);
        using var g = Graphics.FromImage(destination);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(source, 0, 0, width, height);
        return destination;
    }

    /// <summary>按行拷贝为紧致 BGRA 缓冲（SetPixelData 不接受带 stride padding 的数据）。</summary>
    private static byte[] ExtractBgra(Bitmap image)
    {
        var rect = new Rectangle(0, 0, image.Width, image.Height);
        var data = image.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int rowBytes = image.Width * 4;
            var bytes = new byte[rowBytes * image.Height];
            for (int y = 0; y < image.Height; y++)
                Marshal.Copy(data.Scan0 + y * data.Stride, bytes, y * rowBytes, rowBytes);
            return bytes;
        }
        finally
        {
            image.UnlockBits(data);
        }
    }
}
