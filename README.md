# 轻截图 · Light Capture

A small Windows screenshot tool with a global hotkey and a configurable save folder.

**源码可见，仅限非商业用途。** 本项目采用 [PolyForm Noncommercial License 1.0.0](LICENSE)。

选好文件夹，按下快捷键，框选后自动保存。用 C# 和 Windows Forms 编写，无第三方运行时依赖，也不需要安装 Python 或 Node.js。

## 功能

- 选择并记住截图保存文件夹。
- 全局快捷键截图，默认 **Ctrl + Alt + A**，支持自定义和冲突提示。
- 鼠标框选区域，松开即保存；按 **Esc** 或鼠标右键取消。
- 可切换为整个桌面截图，包含所有显示器。
- 自动保存 PNG，文件名带日期、时间与毫秒，重名时追加序号。
- 可同时复制到剪贴板。
- 托盘后台运行，自动记住设置。

## 使用

运行构建得到的 `轻截图.exe`，点击保存位置旁的「选择…」，然后按快捷键截图。

关闭主窗口会隐藏到托盘，快捷键仍然有效。双击托盘图标可打开设置，右键图标或主窗口「退出」按钮可彻底退出。程序运行期间才能响应快捷键。

默认保存目录为系统「图片」文件夹下的 `截图`。本机配置存放在 `%LOCALAPPDATA%\QuickCapture\settings.xml`。

更多操作见 [使用说明](使用说明.md)。

## 从源码构建

目标平台为 Windows 10 / 11，需 .NET Framework 4.x。构建使用系统 .NET Framework 自带的 C# 编译器和 Windows PowerShell，不需要额外安装 .NET SDK 或下载 NuGet 包。

在项目目录运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -OutputDirectory .\dist
```

输出为 `dist\轻截图.exe`。也可省略 `-OutputDirectory`，直接输出到项目目录。

构建目录会同时包含 `LICENSE`、`NOTICE` 和 `README.md`，便于分发时保留许可声明。

运行核心逻辑检查：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\test.ps1
```

仓库包含 GitHub Actions 工作流，在 Windows 上构建、执行检查并保存可执行文件为构建产物；工作流须上传到 GitHub 后才会运行。

## 验证范围与当前限制

核心检查覆盖快捷键规则、反向框选坐标、PNG 像素和尺寸、重名文件保护，以及配置保存和损坏恢复。本机已手动验证选择保存文件夹、默认快捷键框选和 PNG 保存。

这是一个早期的小工具。多显示器混合缩放等场景仍需要更多实机验证；目前不包含滚动截图、图片标注或自动更新。

反馈问题时，请描述 Windows 版本、显示器数量和缩放比例、复现步骤以及预期行为。

## 文件

| 文件 | 用途 |
| --- | --- |
| `ScreenshotTool.cs` | 窗口、快捷键、框选与图片保存 |
| `Tests.cs` | 核心逻辑检查 |
| `build.ps1` | 生成图标并构建可执行文件 |
| `test.ps1` | 编译并运行检查，清理临时测试程序 |
| `.github/workflows/build.yml` | Windows 自动构建和检查 |

## 许可证

本项目的软件和随附文档采用 **PolyForm Noncommercial License 1.0.0**，完整条款见 [LICENSE](LICENSE)，版权声明见 [NOTICE](NOTICE)。

- 允许符合许可证条款的非商业使用、修改和分发，例如无预期商业应用的个人学习、实验和兴趣项目。
- 本许可证不授予商业用途权限，包括以商业目的使用本软件、销售本软件或将其集成到商业产品或收费服务中。
- 分发全部或部分软件（包括修改版）时，须保留许可证条款或其链接，以及 `NOTICE` 中以 `Required Notice:` 开头的声明。

以上为便于理解的摘要，具体许可范围和条件以 `LICENSE` 全文为准。这是限制商业用途的源码可见项目。
