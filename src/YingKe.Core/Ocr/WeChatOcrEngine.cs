using System.Drawing.Imaging;
using System.IO;
using System.Drawing;
using Microsoft.Win32;
using YingKe.Core.Geometry;
using WeChatOcr;

namespace YingKe.Core.Ocr;

/// <summary>
/// 微信 OCR 引擎（参考 STranslate 的 WeChatBuiltIn 插件，基于 WeChatOcr 包装库）。
/// 启动本机安装的微信自带 OCR（WeChatOCR.exe，mmmojo IPC），中文识别质量显著优于 Windows 内置引擎。
/// 依赖：本机已安装微信 PC 版，且微信已下载 OCR 组件（%APPDATA%\Tencent\WeChat\XPlugin）。
/// </summary>
public sealed class WeChatOcrEngine : IOcrEngine
{
    private const int TimeoutMs = 30000; // 首次调用需复制组件并启动 WeChatOCR.exe，放宽超时

    // WeChatOCR.exe 常驻复用：每次重建进程冷启动可达数秒，保活后单次识别几百毫秒
    private static readonly object OcrLock = new();
    private static ImageOcr? _sharedOcr;
    // 共享实例的 Run 串行化：并发调用会交叉回调/超时
    private static readonly SemaphoreSlim RunGate = new(1, 1);

    // 闲置回收计时器：3 分钟无识别时释放常驻 WeChatOCR 子进程
    private const int IdleReleaseMinutes = 3;
    private static System.Threading.Timer? _idleTimer;

    private static ImageOcr GetSharedOcr()
    {
        lock (OcrLock)
        {
            _sharedOcr ??= new ImageOcr();
            return _sharedOcr;
        }
    }

    /// <summary>应用退出时回收常驻微信 OCR 进程（避免孤儿进程）。</summary>
    public static void DisposeShared()
    {
        lock (OcrLock)
        {
            try { _sharedOcr?.Dispose(); } catch { }
            _sharedOcr = null;
        }
    }
    private const string OcrExeName = "WeChatOCR.exe";

    public string Name => "微信 OCR";

    private static string BaseDirectory
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "YingKe", "WeChatOcr");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private static string XPluginDir
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Tencent", "WeChat", "XPlugin", "Plugins", "WeChatOCR");

    /// <summary>微信是否可用：安装目录可定位 且 XPlugin OCR 组件已下载。</summary>
    public static bool IsAvailable()
        => LocateWeChatDir() != null && FindXPluginOcrExe() != null;

    /// <summary>微信安装目录：兼容 3.x（[版本] 括号目录）与 4.x/Weixin（纯版本目录），需包含 mmmojo DLL。</summary>
    public static string? LocateWeChatDir()
    {
        var roots = new List<string>
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Tencent", "WeChat"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tencent", "WeChat"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Tencent", "Weixin"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tencent", "Weixin"),
        };
        foreach (var keyName in new[] { @"HKEY_CURRENT_USER\Software\Tencent\WeChat", @"HKEY_CURRENT_USER\Software\Tencent\Weixin" })
            if (Registry.GetValue(keyName, "InstallPath", null) is string installPath && !string.IsNullOrEmpty(installPath))
                roots.Add(installPath);

        var displayVersion = Registry.GetValue(
            @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\WeChat",
            "DisplayVersion", null) as string;

        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
                continue;
            if (HasMmmojo(root))
                return root; // 3.x 早期布局：mmmojo 直接在根目录

            // 版本子目录：3.x 是 [x.y.z] 括号目录，4.x 是纯版本号目录；取名字最大的（通常最新）
            string? best = null;
            foreach (var sub in Directory.EnumerateDirectories(root).OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase))
            {
                if (HasMmmojo(sub))
                {
                    best = sub;
                    break;
                }
            }
            if (best != null) return best;

            if (!string.IsNullOrEmpty(displayVersion))
            {
                var versioned = Path.Combine(root, "[" + displayVersion + "]");
                if (HasMmmojo(versioned)) return versioned;
            }
        }
        return null;

        static bool HasMmmojo(string dir)
            => File.Exists(Path.Combine(dir, "mmmojo.dll")) || File.Exists(Path.Combine(dir, "mmmojo_64.dll"));
    }

    /// <summary>微信 XPlugin 里自动下载的 WeChatOCR.exe。</summary>
    private static string? FindXPluginOcrExe()
    {
        var dir = XPluginDir;
        if (!Directory.Exists(dir)) return null;
        return Directory.EnumerateFiles(dir, OcrExeName, SearchOption.AllDirectories).FirstOrDefault();
    }

    /// <summary>
    /// 组装 wco_data：XPlugin extracted 全量组件（WeChatOCR.exe + 模型 + 运行库）
    /// + 微信安装目录的 mmmojo_64.dll（微信 4.x 无 32 位 DLL，64 位进程只用后者）。
    /// </summary>
    private static void PrepareWcoData(string ocrExe, string wechatDir)
    {
        var wcoData = Path.Combine(BaseDirectory, "wco_data");
        Directory.CreateDirectory(wcoData);

        var extractedDir = Path.GetDirectoryName(ocrExe);
        if (!string.IsNullOrEmpty(extractedDir) && Directory.Exists(extractedDir))
            CopyDirectory(extractedDir, wcoData);

        CopyIfMissingOrChanged(Path.Combine(wechatDir, "mmmojo_64.dll"), Path.Combine(wcoData, "mmmojo_64.dll"));
        var m32 = Path.Combine(wechatDir, "mmmojo.dll");
        if (File.Exists(m32))
            CopyIfMissingOrChanged(m32, Path.Combine(wcoData, "mmmojo.dll"));
    }

    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        foreach (var file in Directory.EnumerateFiles(sourceDir))
            CopyIfMissingOrChanged(file, Path.Combine(targetDir, Path.GetFileName(file)));
        foreach (var sub in Directory.EnumerateDirectories(sourceDir))
            CopyDirectory(sub, Path.Combine(targetDir, Path.GetFileName(sub)));
    }

    private static void CopyIfMissingOrChanged(string source, string target)
    {
        if (!File.Exists(source)) return;
        if (File.Exists(target))
        {
            var srcInfo = new FileInfo(source);
            var dstInfo = new FileInfo(target);
            if (srcInfo.Length == dstInfo.Length && srcInfo.LastWriteTimeUtc <= dstInfo.LastWriteTimeUtc)
                return;
        }
        File.Copy(source, target, overwrite: true);
    }

    public async Task<OcrResult> RecognizeAsync(Bitmap image, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);

        var ocrExe = FindXPluginOcrExe()
            ?? throw new InvalidOperationException(
                $"未找到微信 OCR 组件（{XPluginDir}\\WeChatOCR.exe）。请在微信里使用一次 OCR 相关功能让微信自动下载组件，或在设置切换其他取字引擎。");
        var wechatDir = LocateWeChatDir()
            ?? throw new InvalidOperationException("未找到微信安装目录（缺少 mmmojo_64.dll），可在设置切换其他取字引擎。");

        PrepareWcoData(ocrExe, wechatDir);
        DataLocation.SetBaseDirectory(BaseDirectory);

        byte[] pngBytes;
        using (var ms = new MemoryStream())
        {
            image.Save(ms, ImageFormat.Png);
            pngBytes = ms.ToArray();
        }

        var tcs = new TaskCompletionSource<OcrResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        // 无参构造：使用 wco_data 中组装好的完整组件（exe + 模型 + mmmojo_64.dll）；
        // 实例常驻复用，避免每次识别冷启动 WeChatOCR.exe；RunGate 串行化并发调用
        var ocr = GetSharedOcr();
        TouchIdleRelease();
        await RunGate.WaitAsync(cancellationToken);
        try
        {
            ocr.Run(pngBytes, (tempPath, result) =>
            {
                try
                {
                    var lines = new List<OcrLine>();
                    var list = result?.OcrResult?.SingleResult;
                    if (list != null)
                    {
                        foreach (var item in list)
                        {
                            if (string.IsNullOrEmpty(item?.SingleStrUtf8))
                                continue;
                            var box = new PixelRect(
                                (int)item.Left, (int)item.Top,
                                (int)(item.Right - item.Left), (int)(item.Bottom - item.Top));
                            lines.Add(new OcrLine(item.SingleStrUtf8, box));
                        }
                    }

                    var text = OcrTextCleaner.Clean(lines.Select(l => l.Text));
                    if (tempPath != null && File.Exists(tempPath))
                    {
                        try { File.Delete(tempPath); } catch { /* 临时文件清理失败不影响结果 */ }
                    }
                    tcs.TrySetResult(new OcrResult(Name, text, lines, "wechat"));
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }, ImageType.Png);

            var timeoutTask = Task.Delay(TimeoutMs, cancellationToken);
            var completed = await Task.WhenAny(tcs.Task, timeoutTask);
            if (completed != tcs.Task)
                throw new TimeoutException($"微信 OCR 超时（{TimeoutMs / 1000} 秒）");
            return tcs.Task.Result;
        }
        finally
        {
            RunGate.Release();
        }
    }

    /// <summary>
    /// 闲置回收：连续 3 分钟无识别时释放常驻 WeChatOCR.exe 子进程与父侧缓冲（约 40MB+）。
    /// 下次识别 GetSharedOcr 会自动重新拉起（冷启动约 1 秒）。
    /// </summary>
    private static void TouchIdleRelease()
    {
        lock (OcrLock) RestartIdleTimer(TimeSpan.FromMinutes(IdleReleaseMinutes));
    }

    private static void RestartIdleTimer(TimeSpan delay)
    {
        _idleTimer?.Dispose();
        _idleTimer = new Timer(_ =>
        {
            // 正在识别时不能强拆：抢不到 RunGate 就顺延后再试
            if (!RunGate.Wait(TimeSpan.Zero))
            {
                lock (OcrLock) RestartIdleTimer(TimeSpan.FromMinutes(1));
                return;
            }
            try
            {
                ImageOcr? shared;
                lock (OcrLock) shared = _sharedOcr;
                if (shared == null) return;
                try { shared.Dispose(); } catch { }
                lock (OcrLock) _sharedOcr = null;
            }
            finally
            {
                RunGate.Release();
            }
        }, null, delay, Timeout.InfiniteTimeSpan);
    }
}
