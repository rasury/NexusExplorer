# 独立音频诊断

此工具用于复现和对比音频播放，不修改正式播放器、数据库、Storage 或主程序配置。

项目直接链接正式 `MediaPlayerService.cs`、播放接口和异常类，在编译时使用同一份源码；LibVLCSharp 版本为 3.9.1。运行时明确加载固定便携目录中的原生 VLC 库，并只读取该目录的硬件解码配置。日志写入工具自己的 `logs` 目录，包括 VLC 输出、音轨和引擎源码 SHA256。

## 操作

1. 关闭其他正在播放声音的程序，打开 `artifacts/audio-diagnostic/NexusExplorer.AudioDiagnostic.exe`。
2. 默认选择老样本 `login.mp3`，也可用“选择音频”选择 WAV、FLAC 等文件。
3. 选择 **A：软件原样（Music 角色）**，点击“从头播放”，听约 30 秒。
4. 点击“停止”，选择 **B：同一引擎，仅改为 Video 角色**，点击“从头播放”，比较同一段。
5. 报告主软件、A、B 是否破音，以及是否存在明显差别。保持听感音量接近。若需要分析日志，提供工具 `logs` 中对应文件。

A 调用生产引擎 `PlayAsync(path, true)`；B 调用同一引擎 `PlayAsync(path, false)`。B 不转换文件、不改变解码格式，只改变软件为媒体设置的角色。两种模式复用同一个播放器，操作顺序与主软件一致：停止并释放，随后播放。诊断窗口没有分类、数据库、主软件的 VideoView 和其他界面负载，因此可以帮助判断这些外围因素是否参与异常。它不等同于官方 VLC 桌面程序。

默认不自动开始播放。关闭窗口等待释放文件句柄。测试媒体始终只读。

## 构建和最小自检

```powershell
dotnet restore tools/AudioDiagnostic/AudioDiagnostic.csproj -r win-x64
dotnet publish tools/AudioDiagnostic/AudioDiagnostic.csproj -c Release -r win-x64 --self-contained true --no-restore -o artifacts/audio-diagnostic
```

在项目目录下运行。原生库来自 `artifacts/NexusExplorer-2.0.4-preview-win-x64/libvlc/win-x64`，不另行下载或替换。`--verify` 在工具目录生成四秒 WAV，以软件音量 0 实际解码 A/B 两种模式、暂停恢复、停止并检查独占文件读取，将结果写入 `verification.json`。这个检查不验证实际听感；破音是否复现需要人工对比。
