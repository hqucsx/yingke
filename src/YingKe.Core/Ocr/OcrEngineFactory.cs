using YingKe.Core.Configuration;
using YingKe.Core.Ocr;

namespace YingKe.Core.Ocr;

/// <summary>按配置产出 OCR 引擎。云端多模态引擎在 M3 随 Provider 客户端一起接入。</summary>
public static class OcrEngineFactory
{
    public static IOcrEngine FromConfig(AppConfig config) => config.Ocr.Engine switch
    {
        OcrEngine.WeChat => new WeChatOcrEngine(),
        OcrEngine.Rapid => new RapidOcrEngine(),
        _ => new WindowsOcrEngine(),
    };
}
