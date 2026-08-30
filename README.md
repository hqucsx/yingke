<p align="center">
  <img src="assets/icon-preview.png" width="96" alt="映刻 icon" />
</p>

<h1 align="center">映刻 YingKe</h1>

<p align="center">
  <b>AI 原生截图工具 · Windows</b><br/>
  截图 · 取字 OCR · AI 识图 · 翻译 · 钉图标注，一气呵成
</p>

<p align="center">
  <img src="https://img.shields.io/badge/platform-Windows%2010%2F11-blue" alt="platform" />
  <img src="https://img.shields.io/badge/.NET-8.0WPF-purple" alt=".NET 8" />
  <img src="https://img.shields.io/badge/license-MIT-green" alt="license" />
</p>

---

**映刻（YingKe）** 是一款面向 Windows 的 AI 原生截图工具：按下快捷键，框选即得——文字一键取走、内容 AI 解读、外文即时翻译、截图钉在桌面。全程本地优先，云端可选。

> 灵感致谢：[kangarooking/Ta](https://github.com/kangarooking/Ta)（macOS 版）与 [STranslate](https://github.com/ZGGSONG/STranslate)。

## ✨ 功能

- **冻结式截图**：热键瞬间拍下全屏再框选，遮罩、放大镜、取色不污染画面；支持多显示器与 Per-Monitor DPI
- **取字（OCR）**：微信 OCR（质量最佳）/ RapidOCR 离线引擎 / Windows 内置 / 云端多模态，识别结果自动进剪贴板
- **AI 识图**：选区交给多模态大模型（OpenAI 兼容 / Azure / Claude / Gemini），自定义提示词模板
- **翻译**：腾讯交互翻译 + Google 双引擎兜底，自动判断中↔英方向，支持双语对照
- **标注**：矩形 / 椭圆 / 箭头 / 画笔 / 文字 / 序号 / 马赛克 / 模糊 / 放大镜取色，24 色调色板 + 撤销
- **钉图**：把选区钉在桌面，缩放、透明度、鼠标穿透、灰度/反色滤镜、双击关闭
- **结果自动复制 + 自动关闭**：识别/翻译完成自动进剪贴板，结果浮窗按自定义延时自动消失
- **内存克制**：常驻十几 MB；OCR 引擎闲置 3 分钟自动回收，截图会话结束即归还内存

## ⌨️ 默认快捷键

| 作用 | 全局 | 选区内 |
| --- | --- | --- |
| 开始截图 | `Ctrl+Shift+Alt+2`（可改为单键，如 `F1`） | — |
| 取字 / AI 识图 / 翻译 | — | `Q` / `I` / `E` |
| 钉图 / 保存 / 复制 | — | `W` / `S` / `Esc`（`Enter` 同复制） |
| 标注：矩形 椭圆 箭头 画笔 | — | `R` `O` `A` `D` |
| 标注：文字 序号 马赛克 模糊 放大 | — | `T` `N` `M` `B` `U` |
| 撤销 / 清空 | — | `Z` / `X` |

所有快捷键均可在设置中改绑，冲突自动检测。

## 📦 安装

1. 从 [Releases](../../releases) 下载 `YingKe-setup-x.y.z.exe`
2. 安装向导为简体中文，**可选安装位置**，可选开机自启与桌面图标
3. 启动后常驻托盘：左键图标 = 设置；右键 = 菜单

## 🚀 快速上手

1. 按 `Ctrl+Shift+Alt+2`（或你设置的热键）框选屏幕
2. 按 `Q` 取字——文字已在剪贴板
3. 选中英文段落按 `E`——译文已在剪贴板
4. 按 `W` 钉在桌面对照抄写，双击关闭
5. 打码：`M` 涂马赛克，`S` 保存

> 取字引擎推荐 **微信 OCR**（需本机安装微信 PC 版，组件自动就位）；离线场景选 **RapidOCR**（首次使用自动下载模型）。AI 识图与云端取字需在「设置 → OCR 与 AI」填入 API Key（存 Windows 凭据管理器，不上传）。

## 🔨 从源码构建

要求：Windows 10 21H2+ / Windows 11、[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)、Inno Setup 6（仅打包需要）。

```bash
git clone https://github.com/<you>/yingke.git
cd yingke

# 编译 + 单元测试
dotnet build YingKe.sln -c Release
dotnet test YingKe.sln -c Release

# 运行（托盘常驻）
dotnet run --project src/YingKe.App -c Release

# 发布单文件 + 打简体中文安装包
dotnet publish src/YingKe.App -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist
iscc installer/yingke.iss
```

## 🧱 项目结构

```
src/YingKe.Core          无 UI 核心库：捕获、OCR 引擎、翻译、配置、凭据、保存路径
src/YingKe.App           WPF 主程序：托盘、热键、冻结覆盖层、标注、钉图、设置中心
tests/YingKe.Core.Tests  xUnit 单元测试（68 例）
installer/yingke.iss     Inno Setup 简体中文安装包脚本
docs/                    PRD 与设计文档
scripts/                 冒烟测试与诊断脚本（PowerShell）
```

## 🔒 隐私

- 本地 OCR（微信 / RapidOCR / Windows 内置）**完全离线**
- API Key 存放于 **Windows 凭据管理器**（当前用户），不写入配置文件
- 截图不上传任何服务器；云端识图仅在你主动触发且配置了 Key 时，把选区图片发送给你自己配置的 API 端点

## 🙏 致谢

- [kangarooking/Ta](https://github.com/kangarooking/Ta) — 最初的灵感来源
- [STranslate](https://github.com/ZGGSONG/STranslate) — 多引擎翻译与微信 OCR 接入思路
- [RapidOCR](https://github.com/RapidAI/RapidOCR) — 离线 OCR 模型
- [腾讯交互翻译](https://transmart.qq.com/) / [Google Translate](https://translate.google.com/) — 翻译服务

## 📄 License

[MIT](LICENSE)
