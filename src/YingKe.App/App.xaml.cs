using System.Diagnostics;
using System.IO;
using System.Windows;
using YingKe.App.Capture;
using YingKe.App.Media;
using YingKe.Core.Capture;
using YingKe.Core.Configuration;
using YingKe.Core.Geometry;
using YingKe.Core.Ocr;
using YingKe.Core.Recognition;

namespace YingKe.App;

public partial class App : Application
{
    private const string AppUserModelID = "YingKe.App";

    private const string SingleInstanceMutexName = "Local\\YingKe.Windows.SingleInstance";

    private Mutex? _singleInstanceMutex;
    private HotkeyWindow? _hotkeyWindow;
    private HotkeyManager? _hotkeyManager;
    private TrayIconService? _tray;
    private OverlayWindow? _overlay;
    private UI.ResultBarWindow? _resultBar;
    private UI.SettingsWindow? _settingsWindow;
    private bool _frozenHandedOff;
    /// <summary>取字/识图/翻译成功后自动关闭结果弹窗的计时器（设置可控，见 AutoCloseResultBar）。</summary>
    private System.Windows.Threading.DispatcherTimer? _autoCloseResultTimer;
    /// <summary>应用退出时取消在途网络请求，避免退出竞态报错。</summary>
    private static readonly System.Threading.CancellationTokenSource NetworkCts = new();
    private readonly List<Capture.PinWindow> _pins = new();
    private DateTime _lastErrorDialogTime = DateTime.MinValue;
    private AppConfig _config = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        // 显式声明 AUMID：通知气泡的 app 名与图标走安装器快捷方式上的同名标记，
        // 不再被 Windows 按 exe 路径自动匹配到历史残留的旧品牌身份
        SetCurrentProcessExplicitAppUserModelID(AppUserModelID);
        base.OnStartup(e);

        // 全局异常只有一个入口（下方带日志与 10 秒节流的处理器）；
        // 不得再叠加无节流的 MessageBox 处理器——异常风暴时会叠出双份弹窗且永远关不完
        _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        // 隐藏消息窗口：接收 WM_HOTKEY 与托盘回调消息
        _hotkeyWindow = new HotkeyWindow();
        _hotkeyWindow.HotkeyPressed += OnHotkeyPressed;
        _hotkeyWindow.Start();

        // 配置（PRD F-45）：%APPDATA%\YingKe\config.json
        _config = AppConfig.Load();

        // 未处理异常：全量落盘 %TEMP%\yingke-error.log；弹窗 10 秒节流，
        // 避免异常风暴时"确定之后又弹出来"导致应用关不掉
        DispatcherUnhandledException += (_, args) =>
        {
            try
            {
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "yingke-error.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {args.Exception}\r\n\r\n");
            }
            catch { /* 日志失败不阻塞 */ }

            if ((DateTime.Now - _lastErrorDialogTime).TotalSeconds >= 10)
            {
                _lastErrorDialogTime = DateTime.Now;
                MessageBox.Show(args.Exception.Message, "映刻 发生错误（详情见 %TEMP%\\yingke-error.log）",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            args.Handled = true;
        };

        // YingKe.exe --selftest：程序内驱动标注管线并做像素断言（自动化验证用）
        if (e.Args.Contains("--selftest"))
        {
            SelfTest.Run(_config);
            Shutdown();
            return;
        }

        // YingKe.exe --apitest：用真实配置走一次带图 Provider 调用（连通性验证）
        if (e.Args.Contains("--apitest"))
        {
            SelfTest.RunApi(_config);
            Shutdown();
            return;
        }

        // YingKe.exe --translatetest：本地 OCR + 内置翻译全链路验证
        if (e.Args.Contains("--translatetest"))
        {
            SelfTest.RunTranslate(_config);
            Shutdown();
            return;
        }

        // YingKe.exe --rapidtest：RapidOCR 离线识别全链路验证（首跑下载模型）
        if (e.Args.Contains("--rapidtest"))
        {
            SelfTest.RunRapid(_config);
            Shutdown();
            return;
        }

        // 快捷键（PRD F-01/F-02）：从配置注册，冲突则气泡提示并可在设置中改键
        _hotkeyManager = new HotkeyManager(_hotkeyWindow.Handle);
        var registered = _hotkeyManager.TrySet(_config.Hotkeys.CaptureModifiers, _config.Hotkeys.CaptureVirtualKey);

        // 托盘常驻：左键 = 设置窗口；右键 = 菜单
        System.Drawing.Icon trayIcon;
        var icoPath = System.IO.Path.Combine(AppContext.BaseDirectory, "yingke.ico");
        trayIcon = System.IO.File.Exists(icoPath)
            ? new System.Drawing.Icon(icoPath)
            : AppIconFactory.Create(); // 发布包缺 yingke.ico 时的兜底
        _tray = new TrayIconService(_hotkeyWindow.Handle, trayIcon, "映刻 - AI 原生截图工具");
        _tray.LeftClicked += OpenSettings;
        _tray.MenuRequested += ShowTrayMenu;
        _hotkeyWindow.ExternalMessageHandler = _tray.HandleWindowMessage;

        var gesture = HotkeyGesture.Describe(_hotkeyManager.Modifiers, _hotkeyManager.VirtualKey);
        _tray.ShowBalloonTip(registered ? "映刻 已在后台运行" : "映刻 已在后台运行（快捷键被占用）",
            registered
                ? $"按 {gesture} 开始截图；左键点托盘图标打开设置，右键菜单可退出。"
                : $"{gesture} 被其他程序占用，请从托盘打开设置更换快捷键。");

        // 后台预热 OCR 引擎（加载模型/拉起组件），让第一次按键就走热路径；
        // 预热图必须带文字笔画（8×8 纯色小图会让检测模型失败，等于没预热）；
        // 云端引擎不预热（避免启动即产生 API 调用）
        if (_config.Ocr.Engine != OcrEngine.CloudModel)
        {
            // 低优先级专用线程：预热不抢占 CPU，拖动选区保持丝滑
            var warmupThread = new System.Threading.Thread(() =>
            {
                try
                {
                    System.Threading.Thread.CurrentThread.Priority =
                        System.Threading.ThreadPriority.BelowNormal;
                    using var warmup = CreateWarmupImage();
                    OcrEngineFactory.FromConfig(_config).RecognizeAsync(warmup)
                        .GetAwaiter().GetResult();
                    AppLog("OCR引擎预热完成");
                    MemoryTrim.Trim(); // 预热产生的 JIT/位图页交还系统
                }
                catch (Exception ex)
                {
                    AppLog($"OCR引擎预热失败: {ex.Message}");
                }
            })
            { IsBackground = true };
            warmupThread.Start();
        }

        // YingKe.exe --settings：启动即打开设置窗口（也便于排查环境问题）
        if (e.Args.Contains("--settings"))
            OpenSettings();

        // 常驻空闲裁剪：托盘菜单/弹窗等零散 UI 也可能让工作集缓涨，
        // 每 5 分钟（且无截图会话进行中）把物理页交还系统，维持常驻低位
        var idleTrimTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(5),
        };
        idleTrimTimer.Tick += (_, _) =>
        {
            if (_overlay == null)
                MemoryTrim.Trim();
        };
        idleTrimTimer.Start();
    }

    private void OnHotkeyPressed(int id)
    {
        if (id == HotkeyWindow.IdCapture)
            StartCapture();
    }

    // ---- 截图 ----

    private void StartCapture()
    {
        if (_overlay != null) return; // 框选进行中，忽略重复触发

        _resultBar?.Close(); // 新一轮截图，收起上一次的结果栏

        FrozenScreen frozen;
        try
        {
            frozen = ScreenCapture.CaptureVirtualScreen();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"屏幕捕获失败：{ex.Message}", "映刻", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _frozenHandedOff = false;
        _overlay = new OverlayWindow(frozen, _config);
        _overlay.ExtractTextRequested += OnExtractTextRequested;
        _overlay.AiVisionRequested += OnAiVisionRequested;
        _overlay.TranslateRequested += OnTranslateRequested;
        _overlay.PinRequested += OnPinRequested;
        _overlay.Closed += (_, _) =>
        {
            _overlay = null;
            if (!_frozenHandedOff)
                frozen.Dispose(); // 取字时所有权已移交 OCR 流程，此处跳过
            MemoryTrim.Trim(); // 截图会话的整屏位图走 LOH，会话结束压缩并交还物理页
        };
        _overlay.Show();
        _overlay.Activate();
    }

    // ---- 取字 / AI 识图 / 翻译（M2 本地 + M3 云端） ----

    private void OnExtractTextRequested(FrozenScreen frozen, PixelRect rect)
    {
        _frozenHandedOff = true;
        _overlay?.Close();

        // 智能路由（PRD F-13）：配置为云端引擎且 Key 可用时，走多模态取字
        if (_config.Ocr.Engine == OcrEngine.CloudModel)
        {
            RunCloudRequest(frozen, rect, "云端取字…", async (provider, crop) =>
            {
                var text = await provider.ChatAsync(PromptTemplates.ExtractText, null, crop);
                return (text, $"云端取字 · {provider.Name}");
            });
            return;
        }

        OpenResultBar(frozen, rect, "识别中…");
        var stopwatch = Stopwatch.StartNew();
        _ = RunLocalOcrAsync(frozen, rect, stopwatch);
    }

    private async Task RunLocalOcrAsync(FrozenScreen frozen, PixelRect rect, Stopwatch stopwatch)
    {
        YingKe.Core.Ocr.IOcrEngine? engine = null;
        try
        {
            using var crop = frozen.Crop(rect);
            engine = OcrEngineFactory.FromConfig(_config);
            OcrResult result;
            try
            {
                result = await engine.RecognizeAsync(crop);
            }
            catch (Exception ex) when (engine is WeChatOcrEngine)
            {
                // 微信 OCR 失败（未装微信/组件缺失/超时）→ 记日志 + 自动回落 Windows 内置引擎
                AppLog($"微信OCR失败: {ex}");

                using var crop2 = frozen.Crop(rect);
                var fallback = new WindowsOcrEngine();
                var fallbackResult = await fallback.RecognizeAsync(crop2);
                var copyNote = TryCopyToClipboard(fallbackResult.Text);
                _resultBar?.ShowResult(fallbackResult.Text,
                    $"微信 OCR 失败（{Truncate(ex.Message, 60)}），已回落 {fallback.Name} · {stopwatch.ElapsedMilliseconds}ms · {copyNote}");
                ScheduleResultAutoClose();
                return;
            }

            if (string.IsNullOrWhiteSpace(result.Text))
            {
                _resultBar?.ShowResult(string.Empty, $"{engine.Name} · 未识别到文字");
                ScheduleResultAutoClose();
                return;
            }

            var copyStatus = TryCopyToClipboard(result.Text);
            AppLog($"取字完成 engine={result.EngineName} 耗时={stopwatch.ElapsedMilliseconds}ms 文字长度={result.Text.Length} copy={copyStatus}");
            _resultBar?.ShowResult(result.Text,
                $"{result.EngineName}（{result.LanguageTag}）· {stopwatch.ElapsedMilliseconds}ms · {copyStatus}");
            ScheduleResultAutoClose();
        }
        catch (Exception ex)
        {
            AppLog($"本地OCR失败（{engine?.Name ?? "未知引擎"}）: {ex}");
            _resultBar?.ShowResult(string.Empty, $"识别失败：{ex.Message}");
        }
        finally
        {
            frozen.Dispose();
        }
    }

    /// <summary>预热用合成图：带文字笔画与网格，真实走一遍检测+识别管线。</summary>
    private static System.Drawing.Bitmap CreateWarmupImage()
    {
        var bitmap = new System.Drawing.Bitmap(400, 240);
        using var g = System.Drawing.Graphics.FromImage(bitmap);
        g.Clear(System.Drawing.Color.White);
        using var font = new System.Drawing.Font("Microsoft YaHei UI", 24f,
            System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Pixel);
        using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.Black);
        g.DrawString("映刻 OCR 预热 Warmup 456", font, brush, 20, 100);
        return bitmap;
    }

    /// <summary>诊断日志开关：设置环境变量 YINGKE_DEBUG=1 时才记录按键/性能等调试噪音，异常日志不受影响。</summary>
    internal static bool DiagnosticsEnabled =>
        Environment.GetEnvironmentVariable("YINGKE_DEBUG") == "1";

    /// <summary>应用日志（性能遥测 + 异常堆栈），供自动化测试与问题诊断读取。</summary>
    internal static void AppLog(string message)
    {
        try
        {
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "yingke-error.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}\r\n");
        }
        catch { /* 日志失败不阻塞 */ }
    }

    /// <summary>
    /// 尽力而为的自动复制：先快速重试（~0.6 秒）；仍失败则转入后台长周期重试
    /// （每 300ms 一次、最长 ~15 秒，成功后自动更新结果栏）——识别结果永不因剪贴板丢失。
    /// 所有写入经全局闸门串行化，避免后台重试与下一次识别的复制互相竞争。
    /// </summary>
    private string TryCopyToClipboard(string text)
    {
        ClipboardHelper.Gate.Wait();
        try
        {
            ClipboardHelper.SetText(text);
            return "已复制到剪贴板";
        }
        catch (Exception ex)
        {
            var bar = _resultBar;
            var note = $"识别成功，但自动复制失败（{Truncate(ex.Message, 40)}）——后台自动重试中，也可点「复制」";
            _ = Task.Run(async () =>
            {
                for (int i = 0; i < 50; i++)
                {
                    await Task.Delay(300);
                    var copied = await Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            ClipboardHelper.Gate.Wait();
                            try { ClipboardHelper.SetText(text); return true; }
                            finally { ClipboardHelper.Gate.Release(); }
                        }
                        catch { return false; }
                    });
                    if (!copied) continue;
                    Dispatcher.Invoke(() =>
                    {
                        if (ReferenceEquals(_resultBar, bar))
                            bar?.ShowResult(text, $"剪贴板曾被占用，后台重试复制成功 · {DateTime.Now:HH:mm:ss}");
                    });
                    return;
                }
            });
            return note;
        }
        finally
        {
            ClipboardHelper.Gate.Release();
        }
    }

    private static string Truncate(string text, int max)
        => string.IsNullOrEmpty(text) || text.Length <= max ? text : text[..max] + "…";

    private void OnAiVisionRequested(FrozenScreen frozen, PixelRect rect, string? templateName)
        => RunCloudRequest(frozen, rect, $"AI 识图 · {templateName}", async (provider, crop) =>
        {
            var prompt = ResolveVisionPrompt(templateName);
            var text = await provider.ChatAsync(prompt, null, crop);
            return (text, $"AI 识图 · {provider.Name} · 模板={templateName}");
        });

    /// <summary>模板名 → 系统提示词（内置注册表或自定义模板）。</summary>
    private string ResolveVisionPrompt(string? templateName)
    {
        if (templateName != null && _config.Ai.CustomPrompts.TryGetValue(templateName, out var custom))
            return custom;
        return PromptTemplates.Templates.TryGetValue(templateName ?? "", out var t) ? t : PromptTemplates.Describe;
    }

    private void OnTranslateRequested(FrozenScreen frozen, PixelRect rect)
    {
        if (_config.Translation.Engine == TranslationEngine.BuiltIn)
        {
            RunBuiltInTranslate(frozen, rect);
            return;
        }
        RunCloudRequest(frozen, rect, $"翻译 → {_config.Translation.TargetLanguage}", async (provider, crop) =>
        {
            // 翻译 = OCR 取原文 + AI 翻译（纯文字模式，PRD F-19）；OCR 用配置引擎，失败回落内置
            string ocrText, ocrName;
            try
            {
                var r = await OcrEngineFactory.FromConfig(_config).RecognizeAsync(crop);
                ocrText = r.Text;
                ocrName = r.EngineName;
            }
            catch
            {
                var fb = await new WindowsOcrEngine().RecognizeAsync(crop);
                ocrText = fb.Text;
                ocrName = fb.EngineName + "（回落）";
            }
            if (string.IsNullOrWhiteSpace(ocrText))
                return (string.Empty, $"{ocrName} 未识别到文字，无法翻译");

            var target = YingKe.Core.Translation.TranslationAuto.Resolve(ocrText, _config.Translation.TargetLanguage);
            var translated = await provider.ChatAsync(
                PromptTemplates.Translate(target, ocrText), null, null, NetworkCts.Token);
            var finalText = _config.Translation.Mode == TranslationMode.Bilingual
                ? translated + "\n\n—— 原文 ——\n" + ocrText
                : translated;
            return (finalText, $"翻译（{ocrName} + {provider.Name}）");
        });
    }

    /// <summary>内置翻译：本地 OCR + 微软免费接口（无需 API Key）。</summary>
    private async void RunBuiltInTranslate(FrozenScreen frozen, PixelRect rect)
    {
        _frozenHandedOff = true;
        _overlay?.Close();
        OpenResultBar(frozen, rect, $"翻译 → {_config.Translation.TargetLanguage}");
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var crop = frozen.Crop(rect);
            string ocrText, ocrName;
            try
            {
                var r = await OcrEngineFactory.FromConfig(_config).RecognizeAsync(crop);
                ocrText = r.Text;
                ocrName = r.EngineName;
            }
            catch
            {
                var fb = await new WindowsOcrEngine().RecognizeAsync(crop);
                ocrText = fb.Text;
                ocrName = fb.EngineName + "（回落）";
            }
            if (string.IsNullOrWhiteSpace(ocrText))
            {
                _resultBar?.ShowResult(string.Empty, $"{ocrName} 未识别到文字，无法翻译");
                ScheduleResultAutoClose();
                return;
            }

            var target = YingKe.Core.Translation.TranslationAuto.Resolve(ocrText, _config.Translation.TargetLanguage);
            AppLog($"翻译方向: {target}，原文 {ocrText.Length} 字");
            var translated = await YingKe.Core.Translation.BuiltInTranslator.TranslateAsync(ocrText, target);
            var finalText = _config.Translation.Mode == TranslationMode.Bilingual
                ? translated + "\n\n—— 原文 ——\n" + ocrText
                : translated;
            var copyStatus = TryCopyToClipboard(finalText);
            _resultBar?.ShowResult(finalText,
                $"翻译（{ocrName} + {YingKe.Core.Translation.BuiltInTranslator.Name}）· {stopwatch.ElapsedMilliseconds}ms · {copyStatus}");
            ScheduleResultAutoClose();
        }
        catch (Exception ex)
        {
            OpenResultBar(frozen, rect, "请求失败");
            _resultBar?.ShowResult(string.Empty, $"翻译失败：{ex.Message}");
        }
        finally
        {
            frozen.Dispose();
        }
    }

    /// <summary>云端请求公共骨架：移交所有权 → 结果栏（识别中）→ Provider 调用 → 复制/展示。</summary>
    private async void RunCloudRequest(FrozenScreen frozen, PixelRect rect, string loadingMeta,
        Func<IVisionLanguageProvider, System.Drawing.Bitmap, Task<(string text, string meta)>> work)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var crop = frozen.Crop(rect);
            var provider = YingKe.Core.Recognition.ProviderFactory.FromConfig(_config)
                ?? throw new InvalidOperationException("未配置 API Key：请从托盘打开设置 → OCR 与 AI 页签保存 Key。");

            OpenResultBar(frozen, rect, loadingMeta);
            var (text, meta) = await work(provider, crop);

            if (string.IsNullOrWhiteSpace(text))
            {
                _resultBar?.ShowResult(string.Empty, meta);
                ScheduleResultAutoClose();
                return;
            }

            var copyStatus = TryCopyToClipboard(text);
            _resultBar?.ShowResult(text, $"{meta} · {stopwatch.ElapsedMilliseconds}ms · {copyStatus}");
            ScheduleResultAutoClose();
        }
        catch (Exception ex)
        {
            OpenResultBar(frozen, rect, "请求失败");
            _resultBar?.ShowResult(string.Empty, $"失败：{ex.Message}");
        }
        finally
        {
            frozen.Dispose();
        }
    }

    /// <summary>
    /// 成功结果（含"未识别到文字"）按设置自动关闭结果弹窗；失败结果由调用方不调度、保持打开。
    /// 每次展示新结果都会重置计时，弹窗被手动关闭时计时器到点后 _resultBar 已为 null，自然无效。
    /// </summary>
    private void ScheduleResultAutoClose()
    {
        if (!_config.General.AutoCloseResultBar)
            return;
        if (_autoCloseResultTimer == null)
        {
            _autoCloseResultTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(Math.Clamp(_config.General.AutoCloseResultSeconds, 1, 60)),
            };
            _autoCloseResultTimer.Tick += (_, _) =>
            {
                _autoCloseResultTimer.Stop();
                _resultBar?.Close(); // 已手动关闭时 _resultBar 为 null，天然无效
            };
        }
        _autoCloseResultTimer.Stop();
        _autoCloseResultTimer.Start();
    }

    private void OpenResultBar(FrozenScreen frozen, PixelRect rect, string loadingMeta)    {
        _resultBar?.Close();
        _autoCloseResultTimer?.Stop(); // 新弹窗打开时停掉上一次的自动关闭计时
        _resultBar = new UI.ResultBarWindow();

        // 结果栏出现在选区右下角下方；按"选区所在显示器"的工作区钳制（多显示器时
        // 虚拟桌面总宽会大于单屏，不能用虚拟桌面整体宽度夹，否则窗口会跨屏悬空）。
        double scale = frozen.Scale;
        var anchor = new YingKe.Core.Native.NativeMethods.NativePoint
        {
            X = frozen.VirtualBounds.X + rect.Right,
            Y = frozen.VirtualBounds.Y + rect.Bottom,
        };
        double x = anchor.X / scale + 14;
        double y = anchor.Y / scale + 14;

        var hMonitor = YingKe.Core.Native.NativeMethods.MonitorFromPoint(
            anchor, YingKe.Core.Native.NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (hMonitor != IntPtr.Zero)
        {
            var info = new YingKe.Core.Native.NativeMethods.MONITORINFO
            {
                cbSize = System.Runtime.InteropServices.Marshal.SizeOf<YingKe.Core.Native.NativeMethods.MONITORINFO>(),
            };
            if (YingKe.Core.Native.NativeMethods.GetMonitorInfo(hMonitor, ref info))
            {
                double workLeft = info.rcWork.Left / scale;
                double workTop = info.rcWork.Top / scale;
                double workRight = info.rcWork.Right / scale;
                double workBottom = info.rcWork.Bottom / scale;
                x = Math.Clamp(x, workLeft + 8, Math.Max(workLeft + 8, workRight - 500));
                y = Math.Clamp(y, workTop + 8, Math.Max(workTop + 8, workBottom - 320));
            }
        }

        _resultBar.Left = x;
        _resultBar.Top = y;
        var bar = _resultBar;
        _resultBar.Closed += (_, _) => { if (ReferenceEquals(_resultBar, bar)) _resultBar = null; MemoryTrim.Trim(); };
        _resultBar.ShowLoading(loadingMeta);
        _resultBar.Show();
    }

    // ---- 设置 ----

    private void OpenSettings()
    {
        if (_settingsWindow != null)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new UI.SettingsWindow(_config, TryApplyCaptureHotkey);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    /// <summary>设置窗口保存快捷键时的回调：注册成功落盘，失败自动回滚旧键。</summary>
    private bool TryApplyCaptureHotkey(uint modifiers, uint virtualKey)
    {
        if (_hotkeyManager == null) return false;
        var oldModifiers = _hotkeyManager.Modifiers;
        var oldVirtualKey = _hotkeyManager.VirtualKey;

        if (!_hotkeyManager.TrySet(modifiers, virtualKey))
        {
            _hotkeyManager.TrySet(oldModifiers, oldVirtualKey); // 回滚
            return false;
        }

        _config.Hotkeys.CaptureModifiers = modifiers;
        _config.Hotkeys.CaptureVirtualKey = virtualKey;
        _config.Save();
        return true;
    }

    // ---- 钉图（PRD F-31/32/33） ----

    private void OnPinRequested(System.Drawing.Bitmap image, Point topLeftDip)
    {
        var pin = new Capture.PinWindow(image, topLeftDip.X, topLeftDip.Y);
        pin.Closed += (_, _) => { _pins.Remove(pin); MemoryTrim.Trim(); };
        _pins.Add(pin);
        pin.Show();
    }

    private void HideOrShowAllPins()
    {
        bool anyVisible = _pins.Any(p => p.IsVisible);
        foreach (var pin in _pins)
        {
            if (anyVisible) pin.Hide();
            else { pin.Show(); }
        }
    }

    private void CloseAllPins()
    {
        foreach (var pin in _pins.ToList())
            pin.Close();
        _pins.Clear();
    }

    private void UnpinAllClickThrough()
    {
        foreach (var pin in _pins)
            pin.SetClickThrough(false);
    }

    // ---- 托盘 ----

    private void ShowTrayMenu()
    {
        if (_overlay != null || _hotkeyWindow == null) return;
        _tray?.ShowContextMenu(_hotkeyWindow.Handle, BuildTrayMenu);
    }

    /// <summary>用户实际使用的保存目录（配置值或默认 图片\YingKe）。</summary>
    private string ResolveSaveDirectoryForUser()
        => string.IsNullOrWhiteSpace(_config.General.SaveDirectory)
            ? YingKe.Core.Files.SavePathGenerator.DefaultDirectory()
            : _config.General.SaveDirectory;

    /// <summary>在资源管理器中打开文件夹（不存在则先创建）。</summary>
    private void OpenFolderInExplorer(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"打开文件夹失败：{ex.Message}", "映刻", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private System.Windows.Controls.ContextMenu BuildTrayMenu()    {
        var menu = new System.Windows.Controls.ContextMenu();

        var settings = new System.Windows.Controls.MenuItem { Header = "设置…" };
        settings.Click += (_, _) => OpenSettings();
        menu.Items.Add(settings);

        var gesture = HotkeyGesture.Describe(_hotkeyManager!.Modifiers, _hotkeyManager.VirtualKey);
        var capture = new System.Windows.Controls.MenuItem { Header = $"截图        {gesture}" };
        capture.Click += (_, _) => StartCapture();
        menu.Items.Add(capture);

        // 钉图管理（PRD F-33：有钉图时出现）
        if (_pins.Count > 0)
        {
            bool anyVisible = _pins.Any(p => p.IsVisible);
            var toggle = new System.Windows.Controls.MenuItem { Header = anyVisible ? "显示全部钉图" : "隐藏全部钉图" };
            toggle.Click += (_, _) => HideOrShowAllPins();
            menu.Items.Add(toggle);

            if (_pins.Any(p => p.IsClickThrough))
            {
                var unpin = new System.Windows.Controls.MenuItem { Header = "取消全部穿透" };
                unpin.Click += (_, _) => UnpinAllClickThrough();
                menu.Items.Add(unpin);
            }

            var closeAll = new System.Windows.Controls.MenuItem { Header = $"关闭全部钉图（{_pins.Count}）" };
            closeAll.Click += (_, _) => CloseAllPins();
            menu.Items.Add(closeAll);

            menu.Items.Add(new System.Windows.Controls.Separator());
        }

        menu.Items.Add(new System.Windows.Controls.Separator());

        var openSaveDir = new System.Windows.Controls.MenuItem { Header = "打开保存目录" };
        openSaveDir.Click += (_, _) => OpenFolderInExplorer(ResolveSaveDirectoryForUser());
        menu.Items.Add(openSaveDir);

        menu.Items.Add(new System.Windows.Controls.Separator());

        var about = new System.Windows.Controls.MenuItem { Header = "映刻 for Windows v0.1.1", IsEnabled = false };
        menu.Items.Add(about);

        var exit = new System.Windows.Controls.MenuItem { Header = "退出" };
        exit.Click += (_, _) => Shutdown();
        menu.Items.Add(exit);

        return menu;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { NetworkCts.Cancel(); } catch { }
        YingKe.Core.Ocr.WeChatOcrEngine.DisposeShared(); // 回收常驻微信 OCR 进程，避免孤儿进程
        _hotkeyManager?.Unregister();
        _tray?.Dispose();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll")]
    private static extern void SetCurrentProcessExplicitAppUserModelID(
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string appID);
}
