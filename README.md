# YT Radio Mini

Windows 上的极简 YouTube 音频播放器：粘贴 YouTube 链接，只播放音频。使用 C# / .NET 10 / WinForms，通过外部 mpv 播放，通过 yt-dlp 展开播放列表。

> 当前为开发版（阶段 1：模块实现完成，尚未在 Windows 上做真实 YouTube 验证，也没有性能实测数据）。发布包与中文用户说明在阶段 5 补全。

## 在 Windows 上运行（开发方式）

前提：已安装 .NET 10 SDK。

1. 准备两个外部程序，放在以下任一位置：
   - 编译输出目录（与 `YTRadioMini.exe` 同一文件夹，例如 `src\YTRadioMini\bin\Debug\net10.0-windows\`）；或
   - 系统 `PATH` 中的某个文件夹。

   | 文件 | 来源 |
   |---|---|
   | `yt-dlp.exe` | https://github.com/yt-dlp/yt-dlp/releases ，请用同一发布页的 `SHA2-256SUMS` 校验 |
   | `mpv.exe` | https://mpv.io/installation/ 列出的 Windows 构建 |

2. 运行：

   ```powershell
   dotnet run --project src/YTRadioMini
   ```

   只看界面、不启动 mpv 和 yt-dlp：

   ```powershell
   dotnet run --project src/YTRadioMini -- --demo
   ```

   `--demo` 下输入任意文本会得到三条模拟曲目；文本含 `bad` 会显示错误。

## 操作

| 操作 | 方式 |
|---|---|
| 添加链接 | 在输入框粘贴或输入后按 Enter，提交框内的全部内容 |
| 输入多个链接 | 一次粘贴多行；或输入一个链接后按 Ctrl+Enter（或 Shift+Enter）另起一行再输入下一个，最后按 Enter 一起提交 |
| 播放 / 暂停 | 左侧圆形按钮；输入框没有焦点时按 Space |
| 下一首 | 右侧圆形按钮；输入框没有焦点时按 N |
| 输入框失去焦点 | 按 Esc，或点击机身 |
| 没有焦点时粘贴 | 直接按 Ctrl+V，内容会进入输入框 |

状态保存在 `%LOCALAPPDATA%\YTRadioMini\state.json`，下次启动恢复队列和位置，但不会自动开始播放。

## 开发与测试

```bash
dotnet build YTRadioMini.sln -c Release
dotnet test tests/YTRadioMini.Tests -c Release
```

测试项目链接主项目中不依赖 WinForms 的源码（`Models`、`Services`、`Application`、`Mocks`），因此可在 Linux 上运行。`MpvServiceIntegrationTests` 在 `PATH` 中找到 `mpv` 时会用真实 mpv 进程测试；找不到时这些测试直接返回，不会报告失败。

目录与职责：

| 目录 | 内容 |
|---|---|
| `src/YTRadioMini/UI` | 窗口与自绘表面，只依赖 `IRadioController` |
| `src/YTRadioMini/Application` | `RadioController`（串行工作循环）、`PlaybackQueue` |
| `src/YTRadioMini/Services` | `MpvService`/`MpvIpcClient`、`YoutubeResolver`、`LinkParser`、`StorageService` |
| `src/YTRadioMini/Mocks` | 供界面演示和测试使用的模拟服务 |
